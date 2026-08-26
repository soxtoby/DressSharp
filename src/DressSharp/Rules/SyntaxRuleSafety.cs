using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

static class SyntaxRuleSafety
{
    internal static bool CanRewrite(SyntaxNode node, RuleContext context) =>
        !context.IsUnsafe(node)
        && HasNoSignificantTrivia(node);

    internal static bool CanRewriteWithoutCounting(SyntaxNode node, RuleContext context) =>
        !context.IsUnsafeWithoutCounting(node)
        && HasNoSignificantTrivia(node);

    static bool HasNoSignificantTrivia(SyntaxNode node) =>
        node.DescendantTrivia(descendIntoTrivia: true).None(trivia =>
            trivia.IsDirective
            || trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
            || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
            || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
            || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));

    internal static SyntaxToken SemicolonFrom(SyntaxToken token) =>
        SyntaxFactory.Token(SyntaxKind.SemicolonToken).WithTrailingTrivia(token.TrailingTrivia);
}

static class GeneratedSyntax
{
    internal static SyntaxAnnotation Block { get; } = new("DressSharp.GeneratedBlock");

    internal static BlockSyntax Mark(BlockSyntax block) => block.WithAdditionalAnnotations(Block);
}
