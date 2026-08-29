using DressSharp.Architecture;
using DressSharp.Rules;

namespace DressSharp.Configuration;

static class PreferenceCatalog
{
    internal static IReadOnlyList<(RuleKey Key, string Default)> Defaults { get; } = RuleCatalog.BuiltIn.Rules
        .Select(rule => (RuleKey: rule.Metadata.RuleKey, rule.Metadata.DefaultValue))
        .ToArray();

    internal static bool IsValid(RuleKey key, string value)
    {
        value = NormalizeValue(key, value);
        return value.Equals("unset", StringComparison.OrdinalIgnoreCase)
            || RuleCatalog.BuiltIn.Rules.Single(rule => rule.Metadata.RuleKey == key).Metadata.Accepts(value);
    }

    internal static string Normalize(RuleKey key, string value) => NormalizeValue(key, value).ToLowerInvariant();

    static string NormalizeValue(RuleKey key, string value)
    {
        value = value.Trim();
        var name = key.ToName();
        return name.StartsWith("csharp_", StringComparison.Ordinal) || name.StartsWith("dotnet_", StringComparison.Ordinal)
            ? WithoutSeverity(value)
            : value;
    }

    static string WithoutSeverity(string value)
    {
        var colon = value.IndexOf(':');
        return colon < 0 ? value : value[..colon].TrimEnd();
    }
}
