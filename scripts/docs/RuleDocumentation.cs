using System.Collections.Immutable;
using DressSharp.Architecture;
using DressSharp.Configuration;
using DressSharp.Interactive;
using DressSharp.Rules;

namespace DressSharp.Docs;

/// <summary>One rendered outcome of a rule's example under one preference value.</summary>
sealed record ExampleOutcome(string Value, string Label, PreviewResult Result, bool IsDefault);

/// <summary>Everything the site shows for one preference, taken from the built-in catalog and the real formatter.</summary>
sealed record RuleDocumentation(
    RuleMetadata Metadata,
    string Key,
    ImmutableArray<ExampleOutcome> Outcomes,
    ImmutableArray<(string Key, string Value)> Companions)
{
    internal bool IsDefaultUnset => Metadata.DefaultValue.Equals("unset", StringComparison.OrdinalIgnoreCase);
}

sealed record RuleGroup(string Name, ImmutableArray<RuleSubgroup> Subgroups)
{
    internal IEnumerable<RuleDocumentation> Rules => Subgroups.SelectMany(subgroup => subgroup.Rules);
}

sealed record RuleSubgroup(string? Name, ImmutableArray<RuleDocumentation> Rules);

static class CatalogReader
{
    internal static int Version => RuleCatalog.BuiltIn.Version;

    /// <summary>Groups are ordered by first appearance in catalog order, which is the order <c>dotnet dress init</c> writes them.</summary>
    internal static async Task<ImmutableArray<RuleGroup>> ReadAsync(CancellationToken cancellationToken)
    {
        var rules = new List<RuleDocumentation>();
        foreach (var rule in RuleCatalog.BuiltIn.Rules)
            rules.Add(await Document(rule.Metadata, cancellationToken));

        return rules.GroupBy(rule => rule.Metadata.GroupName)
            .Select(group => new RuleGroup(
                group.Key,
                group.GroupBy(rule => rule.Metadata.SubgroupName)
                    .Select(subgroup => new RuleSubgroup(subgroup.Key, [.. subgroup]))
                    .ToImmutableArray()))
            .ToImmutableArray();
    }

    /// <summary>Formats an arbitrary snippet under every catalog Default, for the landing page.</summary>
    internal static Task<PreviewResult> FormatWithDefaults(string source, CancellationToken cancellationToken)
    {
        var preferences = RuleCatalog.BuiltIn.Rules
            .Select(rule => rule.Metadata)
            .Where(metadata => !metadata.DefaultValue.Equals("unset", StringComparison.OrdinalIgnoreCase))
            .Select(metadata => Preference(metadata.RuleKey, metadata.DefaultValue))
            .ToArray();
        return InteractivePreview.Format(source, preferences, cancellationToken);
    }

    static async Task<RuleDocumentation> Document(RuleMetadata metadata, CancellationToken cancellationToken)
    {
        var outcomes = new List<ExampleOutcome>();
        foreach (var value in CandidateValues(metadata))
        {
            var preferences = metadata.ExamplePreferences
                .Select(pair => Preference(pair.Key, pair.Value))
                .Append(Preference(metadata.RuleKey, value))
                .ToArray();
            if (Environment.GetEnvironmentVariable("DRESSSHARP_DOCS_TRACE") is not null)
                Console.Error.WriteLine($"{metadata.RuleKey.ToName()} = {value}");
            var result = await InteractivePreview.Format(metadata.Example, preferences, cancellationToken);
            if (result.SkippedOccurrences > 0)
                throw new InvalidOperationException($"Example for {metadata.RuleKey.ToName()} = {value} skipped {result.SkippedOccurrences} occurrence(s).");
            outcomes.Add(new ExampleOutcome(value, Label(metadata, value), result, value.Equals(metadata.DefaultValue, StringComparison.OrdinalIgnoreCase)));
        }

        if (metadata.Values.Kind == RuleValueKind.Integer)
            outcomes = DistinctIntegerOutcomes(outcomes);

        return new RuleDocumentation(
            metadata,
            metadata.RuleKey.ToName(),
            [.. outcomes],
            [.. metadata.ExamplePreferences.Select(pair => (pair.Key.ToName(), pair.Value))]);
    }

    /// <summary>Every value the catalog accepts, or for integers a spread that includes the Default and the special values.</summary>
    static IEnumerable<string> CandidateValues(RuleMetadata metadata)
    {
        var values = metadata.Values;
        var specials = values.SpecialValues.IsDefault ? [] : values.SpecialValues;
        return values.Kind switch
            {
                RuleValueKind.Integer => IntegerCandidates(metadata).Concat(specials),
                RuleValueKind.Permutation => PermutationCandidates(metadata),
                _ => values.Options.Select(option => option.Value).Concat(specials)
            };
    }

    /// <summary>
    /// A spread around the Default: small widths for indentation and blank-line counts, realistic
    /// widths for a line length. A width of one or two would only demonstrate pathological wrapping.
    /// </summary>
    static IEnumerable<string> IntegerCandidates(RuleMetadata metadata)
    {
        var minimum = metadata.Values.Minimum!.Value;
        var isDefaultNumeric = int.TryParse(metadata.DefaultValue, out var @default);
        var spread = isDefaultNumeric && @default >= 20 ? [40, 60, 80, 120, @default] : new[] { minimum, 1, 2, 4, 8 };
        return spread.Where(value => value >= minimum)
            .Select(value => value.ToString())
            .Append(metadata.DefaultValue)
            .Distinct()
            .OrderBy(value => int.TryParse(value, out var number) ? number : int.MaxValue);
    }

    /// <summary>
    /// Every ordering when there are few items; otherwise the Default and its reverse, since the
    /// number of orderings grows factorially and a modifier list has over a dozen items.
    /// </summary>
    static IEnumerable<string> PermutationCandidates(RuleMetadata metadata)
    {
        var items = metadata.Values.Options.Select(option => option.Value).ToArray();
        var defaults = metadata.DefaultValue.Split(',', StringSplitOptions.TrimEntries);
        var candidates = items.Length <= 4
            ? Permutations(items).Select(permutation => string.Join(",", permutation))
            : [metadata.DefaultValue, string.Join(",", defaults.Reverse())];
        return candidates.OrderBy(value => value == metadata.DefaultValue ? 0 : 1);
    }

    /// <summary>Integer values that produce the same text as a neighbour teach nothing, so only the Default and the distinct outcomes remain.</summary>
    static List<ExampleOutcome> DistinctIntegerOutcomes(List<ExampleOutcome> outcomes)
    {
        var kept = new List<ExampleOutcome>();
        foreach (var outcome in outcomes)
        {
            var isSpecial = !int.TryParse(outcome.Value, out _);
            if (outcome.IsDefault || isSpecial || !kept.Any(other => SameOutcome(other, outcome)))
                kept.Add(outcome);
        }
        return kept;
    }

    static bool SameOutcome(ExampleOutcome a, ExampleOutcome b) =>
        a.Result.Text == b.Result.Text
        && a.Result.Encoding == b.Result.Encoding
        && a.Result.LineEndings == b.Result.LineEndings
        && a.Result.FinalNewline == b.Result.FinalNewline;

    static string Label(RuleMetadata metadata, string value) =>
        metadata.Values.Options.FirstOrDefault(option => option.Value.Equals(value, StringComparison.OrdinalIgnoreCase))?.Label ?? value;

    static IEnumerable<string[]> Permutations(string[] items)
    {
        if (items.Length <= 1)
        {
            yield return items;
            yield break;
        }
        for (var i = 0; i < items.Length; i++)
        {
            var rest = items.Where((_, index) => index != i).ToArray();
            foreach (var permutation in Permutations(rest))
                yield return [items[i], .. permutation];
        }
    }

    static InteractivePreference Preference(RuleKey key, string value) =>
        new(key, PreferenceAssignment.Explicit(value), PreferenceAssignment.Absent, null, null, null);
}
