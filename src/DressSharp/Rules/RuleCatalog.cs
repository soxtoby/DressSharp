using System.Collections.Immutable;

namespace DressSharp.Rules;

sealed class RuleCatalog
{
    internal const int CurrentVersion = 1;

    internal RuleCatalog(IEnumerable<IFormattingRule> rules, int version = CurrentVersion)
    {
        if (version <= 0)
            throw new ArgumentOutOfRangeException(nameof(version));

        Version = version;
        Rules = rules.OrderBy(rule => rule.Metadata.Order).ToImmutableArray();
        Validate(Rules);
    }

    internal int Version { get; }
    internal ImmutableArray<IFormattingRule> Rules { get; }

    internal static RuleCatalog BuiltIn { get; } = new([
            new MemberBodyRule("dress_method_body", MemberBodyKind.Method, 100),
            new MemberBodyRule("dress_constructor_body", MemberBodyKind.Constructor, 110),
            new MemberBodyRule("dress_operator_body", MemberBodyKind.Operator, 120),
            new MemberBodyRule("dress_property_body", MemberBodyKind.Property, 130),
            new MemberBodyRule("dress_indexer_body", MemberBodyKind.Indexer, 140),
            new MemberBodyRule("dress_accessor_body", MemberBodyKind.Accessor, 150),
            new LambdaBodyRule(160),
            new NamespaceStyleRule(170),
            new ConditionalBracesRule(180)
        ]);

    static void Validate(ImmutableArray<IFormattingRule> rules)
    {
        if (rules.Any(rule => string.IsNullOrWhiteSpace(rule.Metadata.PreferenceKey) ||
            rule.Metadata.AcceptedValues.IsDefaultOrEmpty ||
            string.IsNullOrWhiteSpace(rule.Metadata.OwnedSyntax) ||
            string.IsNullOrWhiteSpace(rule.Metadata.Invariant) ||
            rule.Metadata.Order < 0))
            throw new ArgumentException("Every rule requires complete catalog metadata.", nameof(rules));

        if (rules.Select(rule => rule.Metadata.PreferenceKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rules.Length)
            throw new ArgumentException("Rule preference keys must be unique.", nameof(rules));
        if (rules.Select(rule => rule.Metadata.Order).Distinct().Count() != rules.Length)
            throw new ArgumentException("Rule order values must be unique.", nameof(rules));
        if (rules.Any(rule => rule.Metadata.AcceptedValues.Distinct(StringComparer.OrdinalIgnoreCase).Count() != rule.Metadata.AcceptedValues.Length))
            throw new ArgumentException("Rule accepted values must be unique.", nameof(rules));
    }
}

enum RuleSafetyClass
{
    Layout,
    SyntaxTransformation
}

sealed record RuleMetadata(
    string PreferenceKey,
    ImmutableArray<string> AcceptedValues,
    string OwnedSyntax,
    RuleSafetyClass SafetyClass,
    string Invariant,
    int Order);

interface IFormattingRule
{
    RuleMetadata Metadata { get; }

    Microsoft.CodeAnalysis.SyntaxNode Transform(
        Microsoft.CodeAnalysis.SyntaxNode root,
        string preference,
        RuleContext context);
}