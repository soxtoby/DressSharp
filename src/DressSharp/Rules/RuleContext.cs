using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DressSharp.Rules;

sealed class RuleContext
{
    readonly ImmutableArray<TextSpan> _malformedRegions;

    internal RuleContext(SyntaxNode root)
    {
        var regions = root.GetDiagnostics()
            .Where(diagnostic => diagnostic.Location.IsInSource)
            .Select(diagnostic => diagnostic.Location.SourceSpan)
            .Concat(root.DescendantTokens(descendIntoTrivia: true)
                .Where(token => token.IsMissing)
                .Select(token => token.Span))
            .Concat(root.DescendantTrivia(descendIntoTrivia: true)
                .Where(trivia => trivia.IsKind(SyntaxKind.SkippedTokensTrivia) || trivia.IsKind(SyntaxKind.DisabledTextTrivia))
                .Select(trivia => trivia.Span));

        _malformedRegions = regions.Distinct().OrderBy(span => span.Start).ToImmutableArray();
    }

    internal bool IsUnsafe(SyntaxNode node) => IsUnsafe(node.FullSpan);
    internal bool IsUnsafe(SyntaxToken token) => IsUnsafe(token.FullSpan);
    internal bool IsUnsafe(TextSpan occurrence) => _malformedRegions.Any(region => Intersects(region, occurrence));

    static bool Intersects(TextSpan left, TextSpan right) =>
        left.IntersectsWith(right)
        || (left.IsEmpty && right.Start <= left.Start && left.Start <= right.End)
        || (right.IsEmpty && left.Start <= right.Start && right.Start <= left.End);
}
