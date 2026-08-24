namespace DressSharp.Rules;

/// <summary>
/// The token gaps changed by syntax-wrapping rules.
/// </summary>
sealed class SyntaxWrappingPlan
{
    readonly string?[] _gaps;

    internal SyntaxWrappingPlan(string?[] gaps, int skippedOccurrences)
    {
        _gaps = gaps;
        SkippedOccurrences = skippedOccurrences;
    }

    internal int SkippedOccurrences { get; }

    internal string? GapBefore(int tokenIndex) =>
        (uint)tokenIndex < (uint)_gaps.Length ? _gaps[tokenIndex] : null;
}
