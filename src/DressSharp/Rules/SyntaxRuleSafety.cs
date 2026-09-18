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

    static bool HasNoSignificantTrivia(SyntaxNode node)
    {
        // Only the trivia attached to tokens is examined. Descending into structured trivia adds
        // nothing: a directive or documentation comment is already rejected by its outer trivia.
        foreach (var token in node.DescendantTokens())
        {
            if (HasSignificantTrivia(token.LeadingTrivia) || HasSignificantTrivia(token.TrailingTrivia))
                return false;
        }

        return true;
    }

    internal static bool HasSignificantTrivia(SyntaxTriviaList trivia)
    {
        for (var index = 0; index < trivia.Count; index++)
        {
            var item = trivia[index];
            if (item.IsDirective
                || item.IsKind(SyntaxKind.SingleLineCommentTrivia)
                || item.IsKind(SyntaxKind.MultiLineCommentTrivia)
                || item.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                || item.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
            {
                return true;
            }
        }

        return false;
    }

    internal static SyntaxToken SemicolonFrom(SyntaxToken token) =>
        SyntaxFactory.Token(SyntaxKind.SemicolonToken).WithTrailingTrivia(token.TrailingTrivia);
}

static class GeneratedSyntax
{
    internal static SyntaxAnnotation Block { get; } = new("DressSharp.GeneratedBlock");

    internal static BlockSyntax Mark(BlockSyntax block) => block.WithAdditionalAnnotations(Block);
}
