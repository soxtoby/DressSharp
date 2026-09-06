using System.Collections.Immutable;
using DressSharp.Architecture;

namespace DressSharp.Rules;

enum RuleValueKind
{
    Boolean,
    Choice,
    MultipleChoice,
    Integer,
    Permutation
}

sealed record RuleValueOption(string Value, string Label, string Description);

sealed record RuleValueDefinition(
    RuleValueKind Kind,
    ImmutableArray<RuleValueOption> Options,
    int? Minimum = null,
    ImmutableArray<string> SpecialValues = default)
{
    internal bool Accepts(string value)
    {
        if (!SpecialValues.IsDefault && SpecialValues.Contains(value, StringComparer.OrdinalIgnoreCase))
            return true;
        return Kind switch
            {
                RuleValueKind.Boolean => bool.TryParse(value, out _),
                RuleValueKind.Integer => int.TryParse(value, out var number) && number >= Minimum,
                RuleValueKind.Choice => Options.Any(option => option.Value.Equals(value, StringComparison.OrdinalIgnoreCase)),
                RuleValueKind.MultipleChoice => IsSelection(value),
                RuleValueKind.Permutation => IsPermutation(value),
                _ => false
            };
    }

    bool IsSelection(string value)
    {
        var selected = Split(value);
        return selected.Length > 0
            && selected.Distinct(StringComparer.OrdinalIgnoreCase).Count() == selected.Length
            && selected.All(item => Options.Any(option => option.Value.Equals(item, StringComparison.OrdinalIgnoreCase)));
    }

    bool IsPermutation(string value)
    {
        var selected = Split(value);
        return selected.Length == Options.Length
            && selected.Distinct(StringComparer.OrdinalIgnoreCase).Count() == Options.Length
            && selected.All(item => Options.Any(option => option.Value.Equals(item, StringComparison.OrdinalIgnoreCase)));
    }

    static string[] Split(string value) => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

static class RuleValues
{
    internal static RuleValueDefinition Boolean() => Choice(RuleValueKind.Boolean, ["true", "false"]);
    internal static RuleValueDefinition Choice(params string[] values) => Choice(RuleValueKind.Choice, values);
    internal static RuleValueDefinition MultipleChoice(ImmutableArray<string> values, params string[] specialValues) =>
        new(RuleValueKind.MultipleChoice, Options(values), null, [.. specialValues]);
    internal static RuleValueDefinition Integer(int minimum, params string[] specialValues) =>
        new(RuleValueKind.Integer, [], minimum, [.. specialValues]);
    internal static RuleValueDefinition Permutation(params string[] values) => Choice(RuleValueKind.Permutation, values);
    internal static RuleValueDefinition From(ImmutableArray<string> forms)
    {
        if (forms is ["non-negative integer"])
            return Integer(0);
        if (forms is ["positive integer"])
            return Integer(1);
        if (forms.Length == 2 && forms.Contains("true") && forms.Contains("false"))
            return Boolean();
        return Choice([.. forms]);
    }

    static RuleValueDefinition Choice(RuleValueKind kind, IEnumerable<string> values) => new(kind, Options(values));
    static ImmutableArray<RuleValueOption> Options(IEnumerable<string> values) => values
        .Select(value => new RuleValueOption(value, Humanize(value), $"Uses `{value}`."))
        .ToImmutableArray();

    static string Humanize(string value)
    {
        var words = value.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select((word, index) => index == 0
            ? char.ToUpperInvariant(word[0]) + word[1..]
            : word));
    }
}

sealed record RuleMetadata
{
    string? _expandedCaption;

    public required RuleKey RuleKey { get; init; }
    public required string Caption { get; init; }
    public string ExpandedCaption { get => _expandedCaption ?? Caption; init => _expandedCaption = value; }
    public required string GroupName { get; init; }
    public string? SubgroupName { get; init; }
    public required string Description { get; init; }
    public required RuleValueDefinition Values { get; init; }
    public required string DefaultValue { get; init; }
    public required string Example { get; init; }
    public required string OwnedSyntax { get; init; }
    public required string Invariant { get; init; }

    internal ImmutableArray<string> AcceptedValueForms => Values.Kind == RuleValueKind.Integer
        ? [Values.Minimum == 0 ? "non-negative integer" : "positive integer", .. Values.SpecialValues.IsDefault ? [] : Values.SpecialValues]
        : [.. Values.Options.Select(option => option.Value), .. Values.SpecialValues.IsDefault ? [] : Values.SpecialValues];
    internal bool Accepts(string value) => Values.Accepts(value);

    internal static string Humanize(string value)
    {
        var words = value.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 1 && words[0] is "csharp" or "dotnet" or "dress")
            words = words[1..];
        return string.Join(' ', words.Select((word, index) => index == 0
            ? char.ToUpperInvariant(word[0]) + word[1..]
            : word));
    }

}

sealed class MetadataRule(RuleMetadata metadata) : IRule
{
    public RuleMetadata Metadata { get; } = metadata;
}
