namespace DressSharp.Rules;

/// <summary>
/// The token gaps changed by syntax-wrapping rules.
/// </summary>
sealed class SyntaxWrappingPlan
{
    readonly string?[] _gaps;
    readonly Dictionary<int, int> _closingOpenings;
    readonly HashSet<int> _trackedOpenings;

    internal SyntaxWrappingPlan(string?[] gaps, int skippedOccurrences, Dictionary<int, int>? closingOpenings = null)
    {
        _gaps = gaps;
        SkippedOccurrences = skippedOccurrences;
        _closingOpenings = closingOpenings ?? [];
        _trackedOpenings = [.. _closingOpenings.Values];
    }

    internal int SkippedOccurrences { get; }

    internal string? GapBefore(int tokenIndex) =>
        (uint)tokenIndex < (uint)_gaps.Length ? _gaps[tokenIndex] : null;

    internal bool TracksOpening(int tokenIndex) => _trackedOpenings.Contains(tokenIndex);
    internal int OpeningForClose(int tokenIndex) => _closingOpenings.GetValueOrDefault(tokenIndex, -1);
}
