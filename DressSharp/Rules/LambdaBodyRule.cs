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
            Description = "Controls lambda bodies. A single return statement and its expression-bodied form represent the same expression.",
            Values = RuleValues.From(["block", "expression"]),
            DefaultValue = "expression",
            Example = """
                class Example
                {
                    Func<int> Value = () => { return 1; };
                }
                """,
            OwnedSyntax = "lambda bodies",
            Invariant = "A single return statement and its expression-bodied form represent the same expression."
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
            // Without type information an expression lambda may bind to either a value- or void-returning delegate.
            // Converting `{ expr; }` to `expr` can change which overload is picked, as can converting an expression body
            // to a block, so only a block holding a single return statement is rewritten.
            if (!expression
                || node.Body is not BlockSyntax { Statements: [ReturnStatementSyntax { Expression: { } returned }] } block
                || !SyntaxRuleSafety.CanRewrite(node, context))
                return node;
            return (T)node.WithBody(returned
                .WithLeadingTrivia(block.OpenBraceToken.LeadingTrivia)
                .WithTrailingTrivia(block.CloseBraceToken.TrailingTrivia));
        }
    }
}
