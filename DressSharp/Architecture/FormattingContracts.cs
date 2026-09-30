using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DressSharp.Architecture;

/// <param name="Contexts">
/// Every distinct way the file is parsed, one per framework of each project that compiles it, in
/// project and framework order. Empty when the file is not formatted.
/// </param>
/// <param name="Diagnostics">What kept the file from being formatted, or is worth saying about how it will be.</param>
/// <param name="Skipped">
/// Whether the file is left alone because a project it may belong to could not be read. The project is
/// reported once, for all of its files, so the file carries no diagnostic and does not fail the run.
/// </param>
sealed record ParseContextResolution(IReadOnlyList<CSharpParseOptions> Contexts, IReadOnlyList<string> Diagnostics, bool Skipped = false)
{
    internal bool CanFormat => Contexts.Count > 0;

    /// <summary>The first context, for a caller that needs only one.</summary>
    internal CSharpParseOptions? Options => Contexts.Count > 0 ? Contexts[0] : null;
}

/// <param name="Files">The resolution of each selected file, by full path.</param>
/// <param name="Warnings">One message per project whose files are skipped, in project path order.</param>
sealed record ResolvedParseContexts(
    IReadOnlyDictionary<string, ParseContextResolution> Files,
    IReadOnlyList<string> Warnings);

sealed record FormattingConfiguration
{
    // Values reach this record already normalized by the resolver (trimmed and lowercased), so the
    // dictionary needs no case-insensitive comparer.
    internal FormattingConfiguration(IEnumerable<KeyValuePair<RuleKey, string>> preferences)
    {
        var builder = ImmutableDictionary.CreateBuilder<RuleKey, string>();
        foreach (var (key, value) in preferences)
        {
            builder.Add(key, value);
            ValueHashCode ^= HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(key.ToString()),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value));
        }

        Preferences = builder.ToImmutable();
    }

    internal ImmutableDictionary<RuleKey, string> Preferences { get; }
    internal int ValueHashCode { get; }
}

sealed class FormattingConfigurationValueComparer : IEqualityComparer<FormattingConfiguration>
{
    internal static FormattingConfigurationValueComparer Instance { get; } = new();

    public bool Equals(FormattingConfiguration? left, FormattingConfiguration? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.Preferences.Count != right.Preferences.Count)
            return false;

        foreach (var (key, value) in left.Preferences)
        {
            if (!right.Preferences.TryGetValue(key, out var other)
                || !value.Equals(other, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public int GetHashCode(FormattingConfiguration configuration) => configuration.ValueHashCode;
}

sealed record TransformationResult(SyntaxNode Root, Exception? Failure = null, int SkippedOccurrences = 0)
{
    [MemberNotNullWhen(false, nameof(Failure))]
    internal bool Succeeded => Failure is null;
}
