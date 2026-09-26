using DressSharp.Architecture;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

sealed class LambdaBodyRule : ISyntaxFormattingRule
{
    public RuleMetadata Metadata { get; } = new()
        {
            RuleKey = RuleKey.DressLambdaBody,
            Caption = "Lambda",
            ExpandedCaption = RuleMetadata.Humanize(RuleKey.DressLambdaBody.ToName()),
            GroupName = "Braces and bodies",
            SubgroupName = "Body styles",
            Description = "Controls lambda bodies. A single return or expression statement and its expression-bodied form represent the same expression.",
            Values = RuleValues.From(["block", "expression"]),
            DefaultValue = "expression",
            Example = """
                class Example
                {
                    Func<int> Value = () => { return 1; };
                }
                """,
            OwnedSyntax = "lambda bodies",
            Invariant = "A single return or expression statement and its expression-bodied form represent the same expression."
        };

    public System.Collections.Immutable.ImmutableArray<SyntaxKind> TargetKinds { get; } =
        [SyntaxKind.SimpleLambdaExpression, SyntaxKind.ParenthesizedLambdaExpression];

    public SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context) =>
        new Rewriter(preference.Equals("expression", StringComparison.OrdinalIgnoreCase), context).Visit(root)!;

    sealed class Rewriter(bool expression, RuleContext context) : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) => Rewrite(node);
        public override SyntaxNode VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) => Rewrite(node);

        T Rewrite<T>(T node) where T : LambdaExpressionSyntax
        {
            if (!CanConvert(node) || !SyntaxRuleSafety.CanRewrite(node, context))
                return node;
            if (expression)
            {
                switch (node.Body)
                {
                    case BlockSyntax { Statements: [ReturnStatementSyntax { Expression: { } returned }] }:
                        return (T)node.WithBody(returned
                            .WithLeadingTrivia(((BlockSyntax)node.Body).OpenBraceToken.LeadingTrivia)
                            .WithTrailingTrivia(((BlockSyntax)node.Body)
                                .CloseBraceToken.TrailingTrivia));
                    case BlockSyntax { Statements: [ExpressionStatementSyntax statement] }:
                        return (T)node.WithBody(statement.Expression
                            .WithLeadingTrivia(((BlockSyntax)node.Body).OpenBraceToken.LeadingTrivia)
                            .WithTrailingTrivia(((BlockSyntax)node.Body).CloseBraceToken.TrailingTrivia));
                }
            }

            // Without type information an expression lambda may bind to either a value- or void-returning delegate.
            // Choosing return versus an expression-statement can change overload resolution, so block conversion is unsafe.
            return node;
        }

        bool CanConvert(LambdaExpressionSyntax node) => expression
            && node.Body is BlockSyntax
            {
                Statements: [ReturnStatementSyntax { Expression: not null } or ExpressionStatementSyntax]
            };
    }
}
