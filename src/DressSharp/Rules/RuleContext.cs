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
        var maximum = configuration.Preferences.TryGetValue(RuleKey.MaxLineLength, out var configuredMaximum)
            && int.TryParse(configuredMaximum, out var parsedMaximum)
                ? parsedMaximum
                : int.MaxValue;
        var tabWidth = configuration.Preferences.TryGetValue(RuleKey.TabWidth, out var configuredTabWidth)
            && int.TryParse(configuredTabWidth, out var parsedTabWidth)
                ? parsedTabWidth
                : 4;
        var usesTabs = configuration.Preferences.TryGetValue(RuleKey.IndentStyle, out var indentStyle)
            && indentStyle.Equals("tab", StringComparison.OrdinalIgnoreCase);
        var indentSize = configuration.Preferences.TryGetValue(RuleKey.IndentSize, out var configuredIndentSize)
            && int.TryParse(configuredIndentSize, out var parsedIndentSize)
                ? parsedIndentSize
                : 4;
        return new(maximum, tabWidth, usesTabs ? "\t" : new string(' ', indentSize));
    }
}

sealed class RuleContext
{
    readonly SyntaxNode _root;
    readonly bool _knownWellFormed;
    ImmutableArray<TextSpan>? _regions;
    int _skippedOccurrences;

    internal RuleContext(SyntaxNode root, RuleSettings settings, bool knownWellFormed = false)
    {
        _root = root;
        _knownWellFormed = knownWellFormed;
        MaximumLineLength = settings.MaximumLineLength;
        TabWidth = settings.TabWidth;
        IndentUnit = settings.IndentUnit;
        LineEnding = FirstLineEnding(root);
    }

    /// <summary>
    /// The spans formatting must not touch, found the first time anything asks.
    /// </summary>
    /// <remarks>
    /// Finding them means reading every diagnostic, token and trivia in the file. Most files are
    /// never asked, because most rules never reach a construct whose safety is in doubt, so paying
    /// for the scan up front is paying for nothing.
    /// </remarks>
    ImmutableArray<TextSpan> MalformedRegions =>
        _regions ??= _knownWellFormed ? [] : ScanMalformedRegions(_root);

    internal int MaximumLineLength { get; }
    internal int TabWidth { get; }
    internal string IndentUnit { get; }
    internal string LineEnding { get; }
    internal bool HasMalformedRegions => !MalformedRegions.IsEmpty;

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
        var regions = MalformedRegions;
        if (regions.IsEmpty)
            return false;

        if (regions.Any(region => Intersects(region, occurrence)))
        {
            _skippedOccurrences++;
            return true;
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

    static ImmutableArray<TextSpan> ScanMalformedRegions(SyntaxNode root)
    {
        if (root is { ContainsDiagnostics: false, ContainsDirectives: false })
            return [];

        var diagnosticRegions = root.GetDiagnostics()
            .Where(diagnostic => diagnostic.Location.IsInSource)
            .Select(diagnostic => diagnostic.Location.SourceSpan);
        if (root.ContainsDirectives)
        {
            diagnosticRegions = diagnosticRegions.Concat(root.DescendantTrivia()
                .Where(trivia => trivia.IsKind(SyntaxKind.DisabledTextTrivia))
                .Select(trivia => trivia.Span));
        }

        return diagnosticRegions.Distinct().OrderBy(span => span.Start).ToImmutableArray();
    }

    static bool Intersects(TextSpan left, TextSpan right) =>
        left.IntersectsWith(right)
        || (left.IsEmpty && right.Start <= left.Start && left.Start <= right.End)
        || (right.IsEmpty && left.Start <= right.Start && right.Start <= left.End);
}
