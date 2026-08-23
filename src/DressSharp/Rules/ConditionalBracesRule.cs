using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

sealed class ConditionalBracesRule : ISyntaxFormattingRule
{
    public RuleMetadata Metadata { get; } = new(
        "dress_conditional_braces",
            ["compact", "always", "balanced"],
        "complete if/else-if/else chains",
        "Branch statements and else-if chain shape are preserved, and braces are removed only when unambiguous.");

    public System.Collections.Immutable.ImmutableArray<SyntaxKind> TargetKinds { get; } = [SyntaxKind.IfStatement];

    public SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context) =>
        new Rewriter(preference.ToLowerInvariant(), context).Visit(root)!;

    sealed class Rewriter(string preference, RuleContext context) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitIfStatement(IfStatementSyntax node)
        {
            // Only the outer node owns a chain; nested else-if nodes are rewritten with it.
            if (node.Parent is ElseClauseSyntax)
                return node;
            if (!CouldChange(node))
                return node;
            if (!SyntaxRuleSafety.CanRewrite(node, context))
                return node;
            var branches = Branches(node).ToArray();
            var canCompact = branches.All(CanUnbrace) && !HasDanglingElseRisk(node);
            var add = preference == "always"
                || preference == "balanced" && !canCompact;
            var remove = preference == "compact"
                || preference == "balanced" && canCompact;
            return RewriteChain(node, add, remove);
        }

        bool CouldChange(IfStatementSyntax node)
        {
            var branches = Branches(node).ToArray();
            if (preference == "always")
                return branches.Any(statement => statement is not BlockSyntax);
            if (preference == "compact")
                return branches.Any(statement => statement is BlockSyntax { Statements: [_] });

            var canCompact = branches.All(statement => statement is not BlockSyntax or BlockSyntax { Statements: [_] })
                && !HasDanglingElseRisk(node);
            return canCompact
                ? branches.Any(statement => statement is BlockSyntax)
                : branches.Any(statement => statement is not BlockSyntax);
        }

        static IEnumerable<StatementSyntax> Branches(IfStatementSyntax root)
        {
            var current = root;
            while (true)
            {
                yield return current.Statement;

                if (current.Else is null)
                    yield break;

                if (current.Else.Statement is not IfStatementSyntax ifStatement)
                {
                    yield return current.Else.Statement;
                    yield break;
                }

                current = ifStatement;
            }
        }

        bool CanUnbrace(StatementSyntax statement) =>
            statement is not BlockSyntax
            || (statement is BlockSyntax { Statements.Count: 1 } block
                && SyntaxRuleSafety.CanRewrite(block, context));

        static bool HasDanglingElseRisk(IfStatementSyntax root)
        {
            var current = root;
            while (true)
            {
                if (current.Else is not null && Unwrapped(current.Statement) is IfStatementSyntax { Else: null })
                    return true;
                if (current.Else?.Statement is not IfStatementSyntax ifStatement)
                    return false;
                current = ifStatement;
            }
        }

        static StatementSyntax Unwrapped(StatementSyntax statement) =>
            statement is BlockSyntax { Statements: [var only] } ? only : statement;

        static IfStatementSyntax RewriteChain(IfStatementSyntax node, bool add, bool remove)
        {
            var danglingElseRisk = node.Else is not null && Unwrapped(node.Statement) is IfStatementSyntax { Else: null };
            var statement = RewriteStatement(node.Statement, add, remove && !danglingElseRisk);
            var elseClause = node.Else switch
                {
                        { Statement: IfStatementSyntax nested } => node.Else.WithStatement(RewriteChain(nested, add, remove)),
                        { } terminal => terminal.WithStatement(RewriteStatement(terminal.Statement, add, remove)),
                    _ => null
                };
            return node
                .WithStatement(statement)
                .WithElse(elseClause);
        }

        static StatementSyntax RewriteStatement(StatementSyntax statement, bool add, bool remove)
        {
            if (add && statement is not BlockSyntax)
                return SyntaxFactory.Block(statement).WithTriviaFrom(statement);
            if (remove && statement is BlockSyntax { Statements: [var single] } block)
                return single
                    .WithLeadingTrivia(block.OpenBraceToken.LeadingTrivia.AddRange(single.GetLeadingTrivia()))
                    .WithTrailingTrivia(single.GetTrailingTrivia().AddRange(block.CloseBraceToken.TrailingTrivia));
            return statement;
        }
    }
}
