using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DressSharp.Rules;

static class SyntaxRuleSafety
{
    internal static bool CanRewrite(SyntaxNode node, RuleContext context) =>
        !context.IsUnsafe(node) 
        &&
        node.DescendantTrivia(descendIntoTrivia: true).None(trivia =>
            trivia.IsDirective 
            || trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) 
            || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
            || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) 
            || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));

    internal static SyntaxToken SemicolonFrom(SyntaxToken token) =>
        SyntaxFactory.Token(token.LeadingTrivia, SyntaxKind.SemicolonToken, token.TrailingTrivia);
}
