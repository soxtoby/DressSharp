using System.Collections.Immutable;
using DressSharp.Architecture;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DressSharp.Rules;

/// <summary>
/// The formatting preferences every rule reads, resolved once per file rather than once per rule.
/// </summary>
sealed record RuleSettings(int MaximumLineLength, int TabWidth, string IndentUnit)
{
    internal static RuleSettings From(FormattingConfiguration configuration)
    {
        var maximum = configuration.Preferences.TryGetValue("max_line_length", out var configuredMaximum)
            && int.TryParse(configuredMaximum, out var parsedMaximum)
                ? parsedMaximum
                : int.MaxValue;
        var tabWidth = configuration.Preferences.TryGetValue("tab_width", out var configuredTabWidth)
            && int.TryParse(configuredTabWidth, out var parsedTabWidth)
                ? parsedTabWidth
                : 4;
        var usesTabs = configuration.Preferences.TryGetValue("indent_style", out var indentStyle)
            && indentStyle.Equals("tab", StringComparison.OrdinalIgnoreCase);
        var indentSize = configuration.Preferences.TryGetValue("indent_size", out var configuredIndentSize)
            && int.TryParse(configuredIndentSize, out var parsedIndentSize)
                ? parsedIndentSize
                : 4;
        return new(maximum, tabWidth, usesTabs ? "\t" : new string(' ', indentSize));
    }
}

sealed class RuleContext
{
    readonly ImmutableArray<TextSpan> _malformedRegions;
    int _skippedOccurrences;

    internal RuleContext(SyntaxNode root, RuleSettings settings, bool knownWellFormed = false)
    {
        MaximumLineLength = settings.MaximumLineLength;
        TabWidth = settings.TabWidth;
        IndentUnit = settings.IndentUnit;
        LineEnding = FirstLineEnding(root);
        _malformedRegions = knownWellFormed ? [] : MalformedRegions(root);
    }

    internal int MaximumLineLength { get; }
    internal int TabWidth { get; }
    internal string IndentUnit { get; }
    internal string LineEnding { get; }
    internal bool HasMalformedRegions => !_malformedRegions.IsEmpty;

    internal int TakeSkippedOccurrences()
    {
        var skipped = _skippedOccurrences;
        _skippedOccurrences = 0;
        return skipped;
    }

    internal bool IsUnsafe(SyntaxNode node) => IsUnsafe(node.FullSpan);
    internal bool IsUnsafe(SyntaxToken token) => IsUnsafe(token.FullSpan);

    internal bool IsUnsafe(TextSpan occurrence)
    {
        // The overwhelmingly common case is a file with no malformed regions at all, and rules ask
        // this per token, so answer it without touching the region list.
        if (_malformedRegions.IsEmpty)
            return false;

        foreach (var region in _malformedRegions)
        {
            if (Intersects(region, occurrence))
            {
                _skippedOccurrences++;
                return true;
            }
        }

        return false;
    }

    static string FirstLineEnding(SyntaxNode root) =>
        root.DescendantTrivia(descendIntoTrivia: true)
                .FirstOrDefault(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                .ToString()
            switch
                {
                    "\r\n" => "\r\n",
                    "\r" => "\r",
                    _ => "\n"
                };

    static ImmutableArray<TextSpan> MalformedRegions(SyntaxNode root)
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

        return regions.Distinct().OrderBy(span => span.Start).ToImmutableArray();
    }

    static bool Intersects(TextSpan left, TextSpan right) =>
        left.IntersectsWith(right)
        || (left.IsEmpty && right.Start <= left.Start && left.Start <= right.End)
        || (right.IsEmpty && left.Start <= right.Start && right.Start <= left.End);
}