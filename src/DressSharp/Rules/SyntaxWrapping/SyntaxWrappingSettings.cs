using DressSharp.Architecture;

namespace DressSharp.Rules;

sealed class SyntaxWrappingSettings
{
    SyntaxWrappingSettings(Setting?[] byKind, bool enabled)
    {
        ByKind = byKind;
        Enabled = enabled;
    }

    internal Setting?[] ByKind { get; }
    internal bool Enabled { get; }

    internal static SyntaxWrappingSettings From(
        IReadOnlyList<SyntaxWrappingRule> rules,
        FormattingConfiguration configuration,
        int maximumLineLength)
    {
        var byKind = new Setting?[(int)SyntaxWrappingKind.Attributes + 1];
        var enabled = false;
        foreach (var rule in rules)
        {
            if (!configuration.Preferences.TryGetValue(rule.Metadata.RuleKey, out var preference)
                || preference.Equals("unset", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!rule.Metadata.AcceptedValueForms.Contains(preference, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Invalid value '{preference}' for '{rule.Metadata.RuleKey.ToName()}'.");
            }

            byKind[(int)rule.Kind] = Setting.For(preference, maximumLineLength);
            enabled = true;
        }

        var style = Preference(RuleKey.DressNestedConditionalStyle);
        var placement = Preference(RuleKey.DotnetStyleOperatorPlacementWhenWrapping);
        Configure(SyntaxWrappingKind.ConditionalExpressions, style);
        Configure(SyntaxWrappingKind.BinaryExpressions, null);
        return new(byKind, enabled);

        string? Preference(RuleKey key) => configuration.Preferences.TryGetValue(key, out var value)
            && !value.Equals("unset", StringComparison.OrdinalIgnoreCase) ? value.ToLowerInvariant() : null;

        void Configure(SyntaxWrappingKind kind, string? nestedStyle)
        {
            if (nestedStyle is null && placement is null)
                return;
            var setting = byKind[(int)kind] ?? new Setting(WrappingMode.Preserve, maximumLineLength);
            byKind[(int)kind] = setting with { NestedStyle = nestedStyle, OperatorPlacement = placement };
            enabled = true;
        }
    }

    internal readonly record struct Setting(WrappingMode Mode, int MaximumLineLength)
    {
        internal string? NestedStyle { get; init; }
        internal string? OperatorPlacement { get; init; }
        internal bool ShapesOperators => NestedStyle is not null || OperatorPlacement is not null;
        internal static Setting For(string preference, int maximumLineLength) => new(
            preference.Equals("compact", StringComparison.OrdinalIgnoreCase)
                ? WrappingMode.Compact
                : preference.Equals("auto", StringComparison.OrdinalIgnoreCase)
                    ? WrappingMode.Auto
                    : preference.Equals("always_multi", StringComparison.OrdinalIgnoreCase)
                        || preference.Equals("expanded", StringComparison.OrdinalIgnoreCase)
                        ? WrappingMode.Multi
                        : WrappingMode.Single,
            preference.Equals("compact", StringComparison.OrdinalIgnoreCase)
            || preference.Equals("auto", StringComparison.OrdinalIgnoreCase)
                ? maximumLineLength
                : int.MaxValue);
    }

    internal enum WrappingMode
    {
        Preserve,
        Single,
        Compact,
        Auto,
        Multi
    }
}
