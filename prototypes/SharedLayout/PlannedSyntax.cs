using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

// PROTOTYPE BRIDGE: let existing syntax-based indentation consumers observe planned positions.
// Reuse syntax structure; only token trivia and changed literal tokens are reconstructed.
static class PlannedSyntax
{
    internal static SyntaxNode Project(SyntaxNode root, SyntaxRewritePlan rewrites, ResolvedTokenLayout layout, CSharpParseOptions options)
    {
        var replacements = rewrites.Replacements.ToDictionary(replacement => replacement.Original);
        var members = root.DescendantNodes().OfType<MemberDeclarationSyntax>()
            .Where(member => replacements.ContainsKey(member.FullSpan)).ToArray();
        root = root.ReplaceNodes(members, (member, _) => replacements[member.FullSpan].Rewritten);
        var leading = new SyntaxTriviaList[layout.Pieces.Length];
        var trailing = new SyntaxTriviaList[layout.Pieces.Length];
        var gaps = new Dictionary<string, (SyntaxTriviaList Trailing, SyntaxTriviaList Leading)>();
        for (var index = 0; index < layout.Pieces.Length; index++)
        {
            var gap = layout.Slice(index == 0 ? 0 : layout.Extent(index - 1).End, layout.Extent(index).Start);
            if (index == 0)
            {
                leading[index] = SyntaxFactory.ParseLeadingTrivia(gap);
                continue;
            }
            if (!gaps.TryGetValue(gap, out var trivia))
            {
                var previous = SyntaxFactory.ParseTrailingTrivia(gap);
                trivia = (previous, SyntaxFactory.ParseLeadingTrivia(gap[previous.FullSpan.Length..]));
                gaps[gap] = trivia;
            }
            trailing[index - 1] = trivia.Trailing;
            leading[index] = trivia.Leading;
        }
        if (trailing.Length > 0) trailing[^1] = SyntaxFactory.ParseTrailingTrivia(layout.Slice(layout.Extent(trailing.Length - 1).End, layout.Length));
        var projection = new Projection(layout, leading, trailing);
        root = projection.Visit(root)!;
        if (projection.Count != layout.Pieces.Length) throw new InvalidOperationException("Projection token count changed");
        return CSharpSyntaxTree.Create((CSharpSyntaxNode)root, options).GetRoot();
    }

    sealed class Projection(ResolvedTokenLayout layout, SyntaxTriviaList[] leading, SyntaxTriviaList[] trailing) : CSharpSyntaxRewriter
    {
        internal int Count { get; private set; }
        public override SyntaxNode? Visit(SyntaxNode? node)
        {
            var result = base.Visit(node);
            return result?.WithoutAnnotations(GeneratedSyntax.Block);
        }

        public override SyntaxToken VisitToken(SyntaxToken token)
        {
            if (token.RawKind == 0) return token;
            var index = Count++;
            if (token.RawKind != layout.Pieces[index].Token.RawKind) throw new InvalidOperationException("Projection token order changed");
            var text = layout.TokenText(index);
            if (token.Text != text)
            {
                var updated = SyntaxFactory.ParseToken(text);
                if (updated.RawKind != token.RawKind) throw new InvalidOperationException("Projected token kind changed");
                token = updated;
            }
            return token.WithLeadingTrivia(leading[index]).WithTrailingTrivia(trailing[index]);
        }
    }
}
