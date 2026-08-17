using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp;

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
            new ConditionalBracesRule(180),
            new UsingOrderRule("dress_global_using_order", 310),
            new UsingOrderRule("dress_using_kind_order", 320),
            new ModifierOrderRule(330),
            new SystemUsingSortRule(372),
            new NewLineRule("csharp_new_line_before_open_brace", NewLineKind.OpenBrace, ["all", "none", "accessors", "anonymous_methods", "anonymous_types", "control_blocks", "events", "indexers", "lambdas", "local_functions", "methods", "object_collection_array_initializers", "properties", "types"], 600),
            new NewLineRule("csharp_new_line_before_else", NewLineKind.Else, ["true", "false"], 601),
            new NewLineRule("csharp_new_line_before_catch", NewLineKind.Catch, ["true", "false"], 602),
            new NewLineRule("csharp_new_line_before_finally", NewLineKind.Finally, ["true", "false"], 603),
            new NewLineRule("csharp_new_line_before_members_in_object_initializers", NewLineKind.ObjectInitializerMembers, ["true", "false"], 604),
            new NewLineRule("csharp_new_line_before_members_in_anonymous_types", NewLineKind.AnonymousTypeMembers, ["true", "false"], 605),
            new NewLineRule("csharp_new_line_between_query_expression_clauses", NewLineKind.QueryClauses, ["true", "false"], 606),
            new CastSpacingRule(374),
            new ControlFlowKeywordSpacingRule(375),
            new ParenthesisSpacingRule(376),
            new BaseListColonSpacingRule(SpacingSide.Before, 377),
            new BaseListColonSpacingRule(SpacingSide.After, 378),
            new BinaryOperatorSpacingRule(379),
            new MethodDeclarationSpacingRule(ParenthesisSpacingKind.Contents, 381),
            new MethodDeclarationSpacingRule(ParenthesisSpacingKind.EmptyContents, 382),
            new MethodDeclarationSpacingRule(ParenthesisSpacingKind.BeforeOpening, 383),
            new MethodCallSpacingRule(ParenthesisSpacingKind.Contents, 384),
            new MethodCallSpacingRule(ParenthesisSpacingKind.EmptyContents, 385),
            new MethodCallSpacingRule(ParenthesisSpacingKind.BeforeOpening, 386),
            new CommaSpacingRule(SpacingSide.After, 387),
            new CommaSpacingRule(SpacingSide.Before, 388),
            new DotSpacingRule(SpacingSide.After, 389),
            new DotSpacingRule(SpacingSide.Before, 390),
            new ForSemicolonSpacingRule(SpacingSide.After, 391),
            new ForSemicolonSpacingRule(SpacingSide.Before, 392),
            new DeclarationSpacingRule(393),
            new BracketSpacingRule(BracketSpacingKind.BeforeOpening, 394),
            new BracketSpacingRule(BracketSpacingKind.Contents, 396)
        ]);

    /// <summary>
    /// Structural rules that act on a whole file rather than on one member, so they cannot be scoped
    /// to a member the way the rest can.
    /// </summary>
    internal RuleCatalog FileScopedStructural => _fileScoped ??= new(
        Structural.Rules.Where(rule => rule is NamespaceStyleRule or UsingOrderRule or SystemUsingSortRule),
        Version);

    /// <summary>
    /// Structural rules whose effect is contained within a single member.
    /// </summary>
    internal RuleCatalog MemberScopedStructural => _memberScoped ??= new(
        Structural.Rules.Where(rule => rule is not (NamespaceStyleRule or UsingOrderRule or SystemUsingSortRule)),
        Version);

    RuleCatalog? _fileScoped;
    RuleCatalog? _memberScoped;

    internal RuleCatalog Structural => _structural ??= new(
        Rules.Where(rule => rule is ISyntaxFormattingRule or UsingOrderRule or ModifierOrderRule or SystemUsingSortRule),
        Version);

    RuleCatalog? _structural;

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

    /// <summary>
    /// The syntax kinds this rule can transform. A file containing none of them cannot be changed by
    /// the rule, so it need not be run over it. An empty set means the rule may always apply.
    /// </summary>
    /// <remarks>
    /// Most rules cost the same to run over a file that has nothing for them as over one that does,
    /// because finding their targets means walking the whole tree. Declaring what they look for lets
    /// that walk be shared and the rule skipped outright.
    /// </remarks>
    ImmutableArray<SyntaxKind> TargetKinds => [];

}

interface ISyntaxFormattingRule : IFormattingRule
{
    Microsoft.CodeAnalysis.SyntaxNode Transform(
        Microsoft.CodeAnalysis.SyntaxNode root,
        string preference,
        RuleContext context);
}
