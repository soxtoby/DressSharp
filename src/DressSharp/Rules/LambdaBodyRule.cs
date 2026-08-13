using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

sealed class LambdaBodyRule(int order) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new(
        "dress_lambda_body",
            ["block", "expression"],
        "lambda bodies",
        RuleSafetyClass.SyntaxTransformation,
        "A single return or expression statement and its expression-bodied form represent the same expression.",
        order);

    public SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context) =>
        new Rewriter(preference.Equals("expression", StringComparison.OrdinalIgnoreCase), context).Visit(root)!;

    sealed class Rewriter(bool expression, RuleContext context) : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) => Rewrite(node);
        public override SyntaxNode VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) => Rewrite(node);

        T Rewrite<T>(T node) where T : LambdaExpressionSyntax
        {
            if (!SyntaxRuleSafety.CanRewrite(node, context))
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
    }
}