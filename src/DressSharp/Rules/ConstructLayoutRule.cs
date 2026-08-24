using DressSharp.Architecture;

namespace DressSharp.Rules;

sealed class ConstructLayoutRule(RuleKey key, ConstructLayoutKind kind) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new(
        key,
        ["always_single", "auto", "always_multi"],
        kind.ToString(),
        "Only whitespace owned by the construct changes");

    internal ConstructLayoutKind Kind => kind;

    internal readonly record struct Setting(ConstructLayoutPreference Preference, int Maximum)
    {
        internal static Setting For(string preference, int maximumLineLength) => new(
            preference.Equals("auto", StringComparison.OrdinalIgnoreCase)
                ? ConstructLayoutPreference.Auto
                : preference.Equals("always_multi", StringComparison.OrdinalIgnoreCase)
                    ? ConstructLayoutPreference.Multi
                    : ConstructLayoutPreference.Single,
            preference.Equals("auto", StringComparison.OrdinalIgnoreCase)
                ? maximumLineLength
                : int.MaxValue);
    }
}

enum ConstructLayoutPreference
{
    Single,
    Auto,
    Multi
}

enum ConstructLayoutKind
{
    Arguments,
    Parameters,
    Initializers,
    CollectionExpressions,
    BaseTypeLists,
    ConstraintClauses,
    MemberAccessChains,
    BinaryExpressions,
    ConditionalExpressions,
    QueryClauses,
    Attributes
}
