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

        return new(byKind, enabled);
    }

    internal readonly record struct Setting(WrappingMode Mode, int MaximumLineLength)
    {
        internal static Setting For(string preference, int maximumLineLength) => new(
            preference.Equals("auto", StringComparison.OrdinalIgnoreCase)
                ? WrappingMode.Auto
                : preference.Equals("always_multi", StringComparison.OrdinalIgnoreCase)
                    ? WrappingMode.Multi
                    : WrappingMode.Single,
            preference.Equals("auto", StringComparison.OrdinalIgnoreCase)
                ? maximumLineLength
                : int.MaxValue);
    }

    internal enum WrappingMode
    {
        Single,
        Auto,
        Multi
    }
}
