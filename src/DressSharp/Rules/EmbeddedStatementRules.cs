using System.Collections.Immutable;
using DressSharp.Architecture;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

sealed record EmbeddedStatementSettings(
    string? Placement,
    EmbeddedStatementBraceMode? Braces,
    bool BracesForMultilineStatementHeader)
{
    internal bool NeedsBracePlanning => Braces is not null || BracesForMultilineStatementHeader;
    internal bool NeedsBraceFreeCandidate => Braces is EmbeddedStatementBraceMode.Compact or EmbeddedStatementBraceMode.Balanced;

    internal static EmbeddedStatementSettings From(FormattingConfiguration configuration) => new(
        PlacementFrom(configuration),
        configuration.Preferences.GetValueOrDefault(RuleKey.DressEmbeddedStatementBraces) switch
            {
                "compact" => EmbeddedStatementBraceMode.Compact,
                "balanced" => EmbeddedStatementBraceMode.Balanced,
                "always" => EmbeddedStatementBraceMode.Always,
                _ => null
            },
        configuration.Preferences.GetValueOrDefault(RuleKey.DressBracesForMultilineStatementHeader) == "true");

    static string? PlacementFrom(FormattingConfiguration configuration)
    {
        var placement = configuration.Preferences.GetValueOrDefault(RuleKey.DressEmbeddedStatementPlacement);
        return placement is "same_line" or "next_line" ? placement : null;
    }
}

sealed class EmbeddedStatementPreferenceRule(
    RuleKey key,
    ImmutableArray<string> values,
    string ownedSyntax,
    string invariant
) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new(key, values, ownedSyntax, invariant);
}

enum EmbeddedStatementBraceMode
{
    Compact,
    Balanced,
    Always
}

static class EmbeddedStatements
{
    internal static bool StartsBody(SyntaxToken token, out StatementSyntax statement)
    {
        var candidate = StartingAt(token);
        if (candidate is null)
        {
            statement = null!;
            return false;
        }

        statement = candidate;
        return OwnerOf(statement) is not null
            && statement is not IfStatementSyntax { Parent: ElseClauseSyntax };
    }

    internal static int UnbracedDepth(StatementSyntax statement)
    {
        var depth = 0;
        StatementSyntax? current = statement;
        if (current is BlockSyntax)
            current = OwnerOf(current) as StatementSyntax;

        while (current is not null && OwnerOf(current) is { } owner)
        {
            depth++;
            current = owner as StatementSyntax;
        }

        return depth;
    }

    static SyntaxNode? OwnerOf(StatementSyntax statement) => statement.Parent switch
        {
            IfStatementSyntax conditional when ReferenceEquals(conditional.Statement, statement) => conditional,
            ElseClauseSyntax alternative when ReferenceEquals(alternative.Statement, statement) => alternative,
            WhileStatementSyntax loop when ReferenceEquals(loop.Statement, statement) => loop,
            DoStatementSyntax loop when ReferenceEquals(loop.Statement, statement) => loop,
            ForStatementSyntax loop when ReferenceEquals(loop.Statement, statement) => loop,
            ForEachStatementSyntax loop when ReferenceEquals(loop.Statement, statement) => loop,
            ForEachVariableStatementSyntax loop when ReferenceEquals(loop.Statement, statement) => loop,
            UsingStatementSyntax scope when ReferenceEquals(scope.Statement, statement) => scope,
            LockStatementSyntax scope when ReferenceEquals(scope.Statement, statement) => scope,
            FixedStatementSyntax scope when ReferenceEquals(scope.Statement, statement) => scope,
            _ => null
        };

    static StatementSyntax? StartingAt(SyntaxToken token)
    {
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            switch (node)
            {
                case StatementSyntax statement:
                    return token == statement.GetFirstToken() ? statement : null;
                case MemberDeclarationSyntax:
                    return null;
            }
        }

        return null;
    }
}

static class EmbeddedStatementBraces
{
    internal static SyntaxNode MinimizeMember(SyntaxNode member, RuleContext context) =>
        new MinimizeRewriter(context).Visit(member)!;

    internal static SyntaxNode ApplyMember(
        SyntaxNode member,
        EmbeddedStatementSettings settings,
        RuleContext context) => new ApplyRewriter(settings, context).Visit(member)!;

    sealed class MinimizeRewriter(RuleContext context) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitIfStatement(IfStatementSyntax node)
        {
            if (!SyntaxRuleSafety.CanRewriteWithoutCounting(node, context))
                return node;
            var current = (IfStatementSyntax)base.VisitIfStatement(node)!;
            var danglingElse = current.Else is not null
                && Unwrapped(current.Statement) is IfStatementSyntax { Else: null };
            var statement = Unbrace(current.Statement, !danglingElse, context);
            var alternative = current.Else is { Statement: not IfStatementSyntax } clause
                ? clause.WithStatement(Unbrace(clause.Statement, true, context))
                : current.Else;
            return current.WithStatement(statement).WithElse(alternative);
        }

        public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node) =>
            Rewrite(node, base.VisitWhileStatement, static (owner, body) => owner.WithStatement(body));

        public override SyntaxNode? VisitDoStatement(DoStatementSyntax node) =>
            Rewrite(node, base.VisitDoStatement, static (owner, body) => owner.WithStatement(body));

        public override SyntaxNode? VisitForStatement(ForStatementSyntax node) =>
            Rewrite(node, base.VisitForStatement, static (owner, body) => owner.WithStatement(body));

        public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node) =>
            Rewrite(node, base.VisitForEachStatement, static (owner, body) => owner.WithStatement(body));

        public override SyntaxNode? VisitForEachVariableStatement(ForEachVariableStatementSyntax node) =>
            Rewrite(node, base.VisitForEachVariableStatement, static (owner, body) => owner.WithStatement(body));

        public override SyntaxNode? VisitUsingStatement(UsingStatementSyntax node) =>
            Rewrite(node, base.VisitUsingStatement, static (owner, body) => owner.WithStatement(body));

        public override SyntaxNode? VisitLockStatement(LockStatementSyntax node) =>
            Rewrite(node, base.VisitLockStatement, static (owner, body) => owner.WithStatement(body));

        public override SyntaxNode? VisitFixedStatement(FixedStatementSyntax node) =>
            Rewrite(node, base.VisitFixedStatement, static (owner, body) => owner.WithStatement(body));

        T Rewrite<T>(T original, Func<T, SyntaxNode?> visit, Func<T, StatementSyntax, T> replace) where T : StatementSyntax
        {
            if (!SyntaxRuleSafety.CanRewriteWithoutCounting(original, context))
                return original;
            var current = (T)visit(original)!;
            return replace(current, Unbrace(Body(current), true, context));
        }
    }

    sealed class ApplyRewriter(EmbeddedStatementSettings settings, RuleContext context) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitIfStatement(IfStatementSyntax node)
        {
            if (!SyntaxRuleSafety.CanRewrite(node, context))
                return node;
            var current = (IfStatementSyntax)base.VisitIfStatement(node)!;

            if (settings.Braces == EmbeddedStatementBraceMode.Balanced
                && ChainBranches(current).Any(branch => RequiresBraces(branch.Owner, branch.Body)))
            {
                return BraceChain(current);
            }

            var danglingElse = current.Else is not null
                && Unwrapped(current.Statement) is IfStatementSyntax { Else: null };
            var statement = CanonicalBody(current, current.Statement, !danglingElse);
            var alternative = current.Else is { Statement: not IfStatementSyntax } clause
                ? clause.WithStatement(CanonicalBody(clause, clause.Statement, true))
                : current.Else;
            return current.WithStatement(statement).WithElse(alternative);
        }

        public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node) =>
            Rewrite(node, base.VisitWhileStatement, static (owner, body) => owner.WithStatement(body));

        public override SyntaxNode? VisitDoStatement(DoStatementSyntax node) =>
            Rewrite(node, base.VisitDoStatement, static (owner, body) => owner.WithStatement(body));

        public override SyntaxNode? VisitForStatement(ForStatementSyntax node) =>
            Rewrite(node, base.VisitForStatement, static (owner, body) => owner.WithStatement(body));

        public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node) =>
            Rewrite(node, base.VisitForEachStatement, static (owner, body) => owner.WithStatement(body));

        public override SyntaxNode? VisitForEachVariableStatement(ForEachVariableStatementSyntax node) =>
            Rewrite(node, base.VisitForEachVariableStatement, static (owner, body) => owner.WithStatement(body));

        public override SyntaxNode? VisitUsingStatement(UsingStatementSyntax node) =>
            Rewrite(node, base.VisitUsingStatement, static (owner, body) => owner.WithStatement(body));

        public override SyntaxNode? VisitLockStatement(LockStatementSyntax node) =>
            Rewrite(node, base.VisitLockStatement, static (owner, body) => owner.WithStatement(body));

        public override SyntaxNode? VisitFixedStatement(FixedStatementSyntax node) =>
            Rewrite(node, base.VisitFixedStatement, static (owner, body) => owner.WithStatement(body));

        T Rewrite<T>(T original, Func<T, SyntaxNode?> visit, Func<T, StatementSyntax, T> replace) where T : StatementSyntax
        {
            if (!SyntaxRuleSafety.CanRewrite(original, context))
                return original;
            var current = (T)visit(original)!;
            return replace(current, CanonicalBody(current, Body(current), true));
        }

        StatementSyntax CanonicalBody(SyntaxNode owner, StatementSyntax body, bool removable)
        {
            if (RequiresBraces(owner, body))
                return Brace(body);
            return settings.Braces is null ? body : Unbrace(body, removable, context);
        }

        bool RequiresBraces(SyntaxNode owner, StatementSyntax body)
        {
            if (settings.Braces == EmbeddedStatementBraceMode.Always)
                return true;
            if (body is BlockSyntax { Statements.Count: > 1 })
                return true;
            if (settings.Braces is EmbeddedStatementBraceMode.Compact or EmbeddedStatementBraceMode.Balanced
                && BodyIsMultiline(body))
            {
                return true;
            }

            return settings.BracesForMultilineStatementHeader && HeaderIsMultiline(owner);
        }

        static bool BodyIsMultiline(StatementSyntax body) => body switch
            {
                BlockSyntax { Statements.Count: 0 } => false,
                BlockSyntax { Statements: [var only] } => IsMultiline(only),
                _ => IsMultiline(body)
            };

        static bool HeaderIsMultiline(SyntaxNode owner) => owner switch
            {
                IfStatementSyntax statement => IsMultiline(statement.IfKeyword, statement.CloseParenToken),
                WhileStatementSyntax statement => IsMultiline(statement.WhileKeyword, statement.CloseParenToken),
                DoStatementSyntax statement => IsMultiline(statement.WhileKeyword, statement.CloseParenToken),
                ForStatementSyntax statement => IsMultiline(statement.ForKeyword, statement.CloseParenToken),
                CommonForEachStatementSyntax statement => IsMultiline(statement.ForEachKeyword, statement.CloseParenToken),
                UsingStatementSyntax statement => IsMultiline(statement.UsingKeyword, statement.CloseParenToken),
                LockStatementSyntax statement => IsMultiline(statement.LockKeyword, statement.CloseParenToken),
                FixedStatementSyntax statement => IsMultiline(statement.FixedKeyword, statement.CloseParenToken),
                _ => false
            };

        static bool IsMultiline(SyntaxNode node) =>
            node.GetLocation().GetLineSpan() is var span
            && span.StartLinePosition.Line != span.EndLinePosition.Line;

        static bool IsMultiline(SyntaxToken first, SyntaxToken last) =>
            first.GetLocation().GetLineSpan().StartLinePosition.Line
            != last.GetLocation().GetLineSpan().EndLinePosition.Line;

        StatementSyntax Brace(StatementSyntax statement)
        {
            if (statement is BlockSyntax)
                return statement;
            var leading = statement.GetLeadingTrivia();
            var trailing = statement.GetTrailingTrivia();
            if (statement is EmptyStatementSyntax)
                return SyntaxFactory.Block().WithLeadingTrivia(leading).WithTrailingTrivia(trailing);
            var inner = statement.WithoutLeadingTrivia().WithoutTrailingTrivia();
            var block = SyntaxFactory.Block(inner);
            if (IsMultiline(statement))
            {
                var lineEnding = SyntaxFactory.EndOfLine(context.LineEnding);
                block = block
                    .WithOpenBraceToken(block.OpenBraceToken.WithTrailingTrivia(lineEnding))
                    .WithStatements(SyntaxFactory.SingletonList(inner.WithTrailingTrivia(lineEnding)));
            }
            else
            {
                block = block
                    .WithOpenBraceToken(block.OpenBraceToken.WithTrailingTrivia(SyntaxFactory.Space))
                    .WithStatements(SyntaxFactory.SingletonList(inner.WithTrailingTrivia(SyntaxFactory.Space)));
            }

            return block.WithLeadingTrivia(leading).WithTrailingTrivia(trailing);
        }

        IfStatementSyntax BraceChain(IfStatementSyntax node)
        {
            var statement = Brace(node.Statement);
            var alternative = node.Else switch
                {
                        { Statement: IfStatementSyntax nested } clause => clause.WithStatement(BraceChain(nested)),
                        { } clause => clause.WithStatement(Brace(clause.Statement)),
                    _ => null
                };
            return node.WithStatement(statement).WithElse(alternative);
        }

        static IEnumerable<(SyntaxNode Owner, StatementSyntax Body)> ChainBranches(IfStatementSyntax root)
        {
            var current = root;
            while (true)
            {
                yield return (current, current.Statement);
                if (current.Else is null)
                    yield break;
                if (current.Else.Statement is not IfStatementSyntax nested)
                {
                    yield return (current.Else, current.Else.Statement);
                    yield break;
                }

                current = nested;
            }
        }
    }

    static StatementSyntax Unbrace(StatementSyntax statement, bool removable, RuleContext context)
    {
        if (!removable
            || statement is not BlockSyntax block
            || block.Statements.Count > 1
            || !SyntaxRuleSafety.CanRewriteWithoutCounting(block, context))
        {
            return statement;
        }

        if (block.Statements.Count == 0)
        {
            return SyntaxFactory.EmptyStatement(
                    SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                .WithLeadingTrivia(block.GetLeadingTrivia())
                .WithTrailingTrivia(block.GetTrailingTrivia());
        }

        return block.Statements[0]
            .WithLeadingTrivia(block.GetLeadingTrivia())
            .WithTrailingTrivia(block.GetTrailingTrivia());
    }

    static StatementSyntax Unwrapped(StatementSyntax statement) =>
        statement is BlockSyntax { Statements: [var only] } ? only : statement;

    static StatementSyntax Body(SyntaxNode owner) => owner switch
        {
            WhileStatementSyntax statement => statement.Statement,
            DoStatementSyntax statement => statement.Statement,
            ForStatementSyntax statement => statement.Statement,
            ForEachStatementSyntax statement => statement.Statement,
            ForEachVariableStatementSyntax statement => statement.Statement,
            UsingStatementSyntax statement => statement.Statement,
            LockStatementSyntax statement => statement.Statement,
            FixedStatementSyntax statement => statement.Statement,
            _ => throw new ArgumentOutOfRangeException(nameof(owner))
        };
}
