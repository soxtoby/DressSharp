using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DressSharp.Rules;

sealed class RuleContext
{
    readonly ImmutableArray<TextSpan> _malformedRegions;
    internal int SkippedOccurrences { get; private set; }

    internal RuleContext(SyntaxNode root, int maximumLineLength = int.MaxValue, int tabWidth = 4, string indentUnit = "    ")
    {
        MaximumLineLength = maximumLineLength;
        TabWidth = tabWidth;
        IndentUnit = indentUnit;
        LineEnding = root.DescendantTrivia(descendIntoTrivia: true)
                .FirstOrDefault(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                .ToString() switch
                {
                    "\r\n" => "\r\n",
                    "\r" => "\r",
                    _ => "\n"
                };
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

    internal int MaximumLineLength { get; }
    internal int TabWidth { get; }
    internal string IndentUnit { get; }
    internal string LineEnding { get; }

    internal bool IsUnsafe(SyntaxNode node) => IsUnsafe(node.FullSpan);
    internal bool IsUnsafe(SyntaxToken token) => IsUnsafe(token.FullSpan);
    internal bool IsUnsafe(TextSpan occurrence)
    {
        if (!_malformedRegions.Any(region => Intersects(region, occurrence)))
            return false;
        SkippedOccurrences++;
        return true;
    }

    static bool Intersects(TextSpan left, TextSpan right) =>
        left.IntersectsWith(right)
        || (left.IsEmpty && right.Start <= left.Start && left.Start <= right.End)
        || (right.IsEmpty && left.Start <= right.Start && right.Start <= left.End);
}
