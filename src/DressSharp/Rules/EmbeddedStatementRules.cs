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
    string caption,
    string? subgroupName,
    ImmutableArray<string> values,
    string defaultValue,
    string ownedSyntax,
    string invariant,
    string? description = null,
    string? expandedCaption = null
) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new()
        {
            RuleKey = key,
            Caption = caption,
            ExpandedCaption = expandedCaption ?? RuleMetadata.Humanize(key.ToName()),
            GroupName = "Braces and bodies",
            SubgroupName = subgroupName,
            Description = description ?? $"Controls {ownedSyntax}. {invariant}.",
            Values = RuleValues.From(values),
            DefaultValue = defaultValue,
            Example = key == RuleKey.DressBracesForMultilineStatementHeader
                ? "if (firstCondition\n    && secondCondition)\n    Work();"
                : """
                class Example
                {
                    void Run() { if (true) Work(); }
                    void Work() { }
                }
                """,
            OwnedSyntax = ownedSyntax,
            Invariant = invariant
        };
}

enum EmbeddedStatementBraceMode
{
    Compact,
    Balanced,
    Always
}

static class EmbeddedStatements
{
    /// <summary>
    /// Whether <paramref name="candidate"/>, the statement starting at the token in hand, is the
    /// body a statement header owns.
    /// </summary>
    internal static bool StartsBody(StatementSyntax? candidate, out StatementSyntax statement)
    {
        if (candidate is null)
        {
            statement = null!;
            return false;
        }

        statement = candidate;
        return OwnerOf(statement) is not null
            && statement is not IfStatementSyntax { Parent: ElseClauseSyntax };
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
}

static class EmbeddedStatementBraces
{
    internal static SyntaxNode MinimizeMember(SyntaxNode member, RuleContext context) =>
        OwnsAnEmbeddedStatement(member) ? new MinimizeRewriter(context).Visit(member)! : member;

    /// <summary>
    /// Braces the embedded statements in a member, asking <paramref name="lines"/> where the
    /// planned layout breaks rather than reading the file back once it has been written.
    /// </summary>
    internal static SyntaxNode ApplyMember(
        SyntaxNode member,
        int segment,
        EmbeddedStatementSettings settings,
        RuleContext context,
        SinglePassEmitter lines) =>
        OwnsAnEmbeddedStatement(member)
            ? new ApplyRewriter(settings, context, lines, segment).Visit(member)!
            : member;

    /// <summary>
    /// Whether the member contains a statement that owns an embedded body.
    /// </summary>
    /// <remarks>
    /// Both rewriters visit only these nine statements, so a member without one is returned
    /// unchanged. Every member is offered to them on each brace pass, and a rewriter walk dispatches
    /// through every node in the member; looking first for the kinds that matter is cheaper, and
    /// members with no statements at all — fields, auto-properties, signatures — are most of a file.
    /// </remarks>
    static bool OwnsAnEmbeddedStatement(SyntaxNode member)
    {
        foreach (var node in member.DescendantNodes())
        {
            switch (node.Kind())
            {
                case SyntaxKind.IfStatement:
                case SyntaxKind.WhileStatement:
                case SyntaxKind.DoStatement:
                case SyntaxKind.ForStatement:
                case SyntaxKind.ForEachStatement:
                case SyntaxKind.ForEachVariableStatement:
                case SyntaxKind.UsingStatement:
                case SyntaxKind.LockStatement:
                case SyntaxKind.FixedStatement:
                    return true;
            }
        }

        return false;
    }

    sealed class MinimizeRewriter(RuleContext context) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitIfStatement(IfStatementSyntax node)
        {
            if (!SyntaxRuleSafety.CanRewriteWithoutCounting(node, context))
                return node;
            var current = (IfStatementSyntax)base.VisitIfStatement(node)!;
            var danglingElse = current.Else is not null
                && HasUnmatchedIf(Unwrapped(current.Statement));
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

    sealed class ApplyRewriter(
        EmbeddedStatementSettings settings,
        RuleContext context,
        SinglePassEmitter lines,
        int segment) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitIfStatement(IfStatementSyntax node)
        {
            if (!SyntaxRuleSafety.CanRewrite(node, context, segment < 0))
                return node;

            // Every question about lines is put to the statement as the plan laid it out, before
            // this pass has changed anything inside it.
            var shape = ShapeOf(node, node.Statement);
            var alternativeShape = node.Else is { Statement: not IfStatementSyntax } original
                ? ShapeOf(node.Else, original.Statement)
                : default;
            var bracesTheChain = settings.Braces == EmbeddedStatementBraceMode.Balanced
                && ChainBranches(node).Any(branch =>
                    RequiresBraces(ShapeOf(branch.Owner, branch.Body), branch.Body));
            var current = (IfStatementSyntax)base.VisitIfStatement(node)!;
            if (bracesTheChain)
                return BraceChain(current, node);

            var danglingElse = current.Else is not null
                && HasUnmatchedIf(Unwrapped(current.Statement));
            var statement = CanonicalBody(current.Statement, shape, !danglingElse);
            var alternative = current.Else is { Statement: not IfStatementSyntax } clause
                ? clause.WithStatement(CanonicalBody(clause.Statement, alternativeShape, true))
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
            if (!SyntaxRuleSafety.CanRewrite(original, context, segment < 0))
                return original;
            var shape = ShapeOf(original, Body(original));
            var current = (T)visit(original)!;
            return replace(current, CanonicalBody(Body(current), shape, true));
        }

        StatementSyntax CanonicalBody(StatementSyntax body, Shape shape, bool removable)
        {
            if (RequiresBraces(shape, body))
                return Brace(body, shape.Body);
            return settings.Braces is null ? body : Unbrace(body, removable, context);
        }

        bool RequiresBraces(Shape shape, StatementSyntax body)
        {
            if (settings.Braces == EmbeddedStatementBraceMode.Always)
                return true;
            if (body is BlockSyntax { Statements.Count: > 1 })
                return true;
            if (settings.Braces is EmbeddedStatementBraceMode.Compact or EmbeddedStatementBraceMode.Balanced
                && shape.Body)
            {
                return true;
            }

            return settings.BracesForMultilineStatementHeader && shape.Header;
        }

        /// <summary>
        /// How a statement header and the body it owns are about to be laid out.
        /// </summary>
        readonly record struct Shape(bool Header, bool Body);

        Shape ShapeOf(SyntaxNode owner, StatementSyntax body) =>
            new(HeaderSpansLines(owner), BodySpansLines(body));

        bool BodySpansLines(StatementSyntax body) => body switch
            {
                BlockSyntax { Statements.Count: 0 } => false,
                BlockSyntax { Statements: [var only] } => SpansLines(only),
                _ => SpansLines(body)
            };

        bool HeaderSpansLines(SyntaxNode owner) => owner switch
            {
                IfStatementSyntax statement => SpansLines(statement.IfKeyword, statement.CloseParenToken),
                WhileStatementSyntax statement => SpansLines(statement.WhileKeyword, statement.CloseParenToken),
                DoStatementSyntax statement => SpansLines(statement.WhileKeyword, statement.CloseParenToken),
                ForStatementSyntax statement => SpansLines(statement.ForKeyword, statement.CloseParenToken),
                CommonForEachStatementSyntax statement => SpansLines(statement.ForEachKeyword, statement.CloseParenToken),
                UsingStatementSyntax statement => SpansLines(statement.UsingKeyword, statement.CloseParenToken),
                LockStatementSyntax statement => SpansLines(statement.LockKeyword, statement.CloseParenToken),
                FixedStatementSyntax statement => SpansLines(statement.FixedKeyword, statement.CloseParenToken),
                _ => false
            };

        bool SpansLines(SyntaxNode node) => lines.SpansLines(node, segment);

        bool SpansLines(SyntaxToken first, SyntaxToken last) =>
            lines.SpansLines(first, last, segment)
            ?? first.GetLocation().GetLineSpan().StartLinePosition.Line
                != last.GetLocation().GetLineSpan().EndLinePosition.Line;

        StatementSyntax Brace(StatementSyntax statement, bool spansLines)
        {
            if (statement is BlockSyntax)
                return statement;
            var leading = statement.GetLeadingTrivia();
            var trailing = statement.GetTrailingTrivia();
            if (statement is EmptyStatementSyntax)
            {
                return GeneratedSyntax.Mark(SyntaxFactory.Block())
                    .WithLeadingTrivia(leading)
                    .WithTrailingTrivia(trailing);
            }
            var inner = statement.WithoutLeadingTrivia().WithoutTrailingTrivia();
            var block = GeneratedSyntax.Mark(SyntaxFactory.Block(inner));
            if (spansLines)
            {
                var lineEnding = SyntaxFactory.EndOfLine(context.LineEnding);
                // Keep the source anchor for continuation lines; emission will reindent the body.
                var indentation = leading.Reverse().TakeWhile(trivia => trivia.IsKind(SyntaxKind.WhitespaceTrivia)).Reverse();
                block = block
                    .WithOpenBraceToken(block.OpenBraceToken.WithTrailingTrivia(lineEnding))
                    .WithStatements(SyntaxFactory.SingletonList(inner
                        .WithLeadingTrivia(indentation)
                        .WithTrailingTrivia(lineEnding)));
            }
            else
            {
                block = block
                    .WithOpenBraceToken(block.OpenBraceToken.WithTrailingTrivia(SyntaxFactory.Space))
                    .WithStatements(SyntaxFactory.SingletonList(inner.WithTrailingTrivia(SyntaxFactory.Space)));
            }

            return block.WithLeadingTrivia(leading).WithTrailingTrivia(trailing);
        }

        IfStatementSyntax BraceChain(IfStatementSyntax node, IfStatementSyntax original)
        {
            var statement = Brace(node.Statement, BodySpansLines(original.Statement));
            ElseClauseSyntax? alternative = null;
            if (node.Else is { } clause && original.Else is { } originalClause)
            {
                alternative = clause.Statement is IfStatementSyntax nested
                    && originalClause.Statement is IfStatementSyntax originalNested
                        ? clause.WithStatement(BraceChain(nested, originalNested))
                        : clause.WithStatement(Brace(clause.Statement, BodySpansLines(originalClause.Statement)));
            }

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
            .WithLeadingTrivia(Rebased(block.GetLeadingTrivia(), block.Statements[0].GetLeadingTrivia()))
            .WithTrailingTrivia(block.GetTrailingTrivia());
    }

    /// <summary>
    /// The trivia that stood before a pair of braces, ending at the column the statement inside
    /// them began in.
    /// </summary>
    /// <remarks>
    /// A statement taken out of its braces still carries the lines its own continuations were
    /// written against. Starting it where the brace was, without moving those, would lose the
    /// offsets between them, and with them every column later measured from where it begins.
    /// </remarks>
    static SyntaxTriviaList Rebased(SyntaxTriviaList before, SyntaxTriviaList inside)
    {
        var indent = inside.Count;
        while (indent > 0 && inside[indent - 1].IsKind(SyntaxKind.WhitespaceTrivia))
            indent--;
        // Nothing to rebase when the statement began on the brace's own line: whatever stood
        // between them is the brace's trailing trivia, not an indent of the statement's.
        if (indent == inside.Count)
            return before;

        var kept = before.Count;
        while (kept > 0 && before[kept - 1].IsKind(SyntaxKind.WhitespaceTrivia))
            kept--;
        return SyntaxFactory.TriviaList(before.Take(kept).Concat(inside.Skip(indent)));
    }

    static StatementSyntax Unwrapped(StatementSyntax statement) =>
        statement is BlockSyntax { Statements: [var only] } ? only : statement;

    // A following else can bind through an else branch or an unbraced loop/scope body.
    // Blocks and do/while statements close that path.
    static bool HasUnmatchedIf(StatementSyntax statement) => statement switch
        {
            IfStatementSyntax conditional => conditional.Else is null || HasUnmatchedIf(conditional.Else.Statement),
            WhileStatementSyntax loop => HasUnmatchedIf(loop.Statement),
            ForStatementSyntax loop => HasUnmatchedIf(loop.Statement),
            CommonForEachStatementSyntax loop => HasUnmatchedIf(loop.Statement),
            UsingStatementSyntax scope => HasUnmatchedIf(scope.Statement),
            LockStatementSyntax scope => HasUnmatchedIf(scope.Statement),
            FixedStatementSyntax scope => HasUnmatchedIf(scope.Statement),
            _ => false
        };

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
