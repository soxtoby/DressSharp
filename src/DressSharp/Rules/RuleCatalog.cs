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
            new ConditionalBracesRule(180),
            new BlankLineRule("dress_blank_lines_around_namespaces", BlankLineKind.AroundNamespaces, 200),
            new BlankLineRule("dress_blank_lines_around_types", BlankLineKind.AroundTypes, 210),
            new BlankLineRule("dress_blank_lines_between_members", BlankLineKind.BetweenMembers, 220),
            new BlankLineRule("dress_blank_lines_between_using_groups", BlankLineKind.BetweenUsingGroups, 230),
            new BlankLineRule("dress_blank_lines_between_member_categories", BlankLineKind.BetweenMemberCategories, 240),
            new BlankLineRule("dress_max_consecutive_blank_lines", BlankLineKind.Maximum, 250),
            new CommentRule("dress_line_comment_spacing", CommentKind.LineSpacing, ["none", "single"], 260),
            new CommentRule("dress_block_comment_spacing", CommentKind.BlockSpacing, ["none", "single"], 270),
            new CommentRule("dress_attached_comment_placement", CommentKind.AttachedPlacement, ["same_line", "own_line", "auto"], 280),
            new CommentRule("dress_xml_comment_placement", CommentKind.XmlPlacement, ["attached", "separated"], 290),
            new CommentRule("dress_xml_element_layout", CommentKind.XmlElementLayout, ["single_line", "multi_line"], 300),
            new UsingOrderRule("dress_global_using_order", 310),
            new UsingOrderRule("dress_using_kind_order", 320),
            new ModifierOrderRule(330),
            new InitializerIndentationRule("dress_object_initializer_indentation", InitializerKind.Object, 340),
            new InitializerIndentationRule("dress_collection_initializer_indentation", InitializerKind.Collection, 350),
            new InitializerIndentationRule("dress_array_initializer_indentation", InitializerKind.Array, 360),
            new InitializerIndentationRule("dress_with_initializer_indentation", InitializerKind.With, 370),
            // Kept with its sibling initializer rules rather than at 380, which fell inside the
            // token-spacing range and split those rules into two passes over the file.
            new InitializerIndentationRule("dress_collection_expression_indentation", InitializerKind.CollectionExpression, 371),
            new NewLineRule("csharp_new_line_before_open_brace", NewLineKind.OpenBrace, ["all", "none", "accessors", "anonymous_methods", "anonymous_types", "control_blocks", "events", "indexers", "lambdas", "local_functions", "methods", "object_collection_array_initializers", "properties", "types"], 600),
            new NewLineRule("csharp_new_line_before_else", NewLineKind.Else, ["true", "false"], 601),
            new NewLineRule("csharp_new_line_before_catch", NewLineKind.Catch, ["true", "false"], 602),
            new NewLineRule("csharp_new_line_before_finally", NewLineKind.Finally, ["true", "false"], 603),
            new NewLineRule("csharp_new_line_before_members_in_object_initializers", NewLineKind.ObjectInitializerMembers, ["true", "false"], 604),
            new NewLineRule("csharp_new_line_before_members_in_anonymous_types", NewLineKind.AnonymousTypeMembers, ["true", "false"], 605),
            new NewLineRule("csharp_new_line_between_query_expression_clauses", NewLineKind.QueryClauses, ["true", "false"], 606),
            new IndentationRule("csharp_indent_switch_labels", IndentationKind.SwitchLabels, ["true", "false"], 607),
            new IndentationRule("csharp_indent_case_contents", IndentationKind.CaseContents, ["true", "false"], 608),
            new IndentationRule("csharp_indent_labels", IndentationKind.Labels, ["flush_left", "no_change", "one_less_than_current"], 609),
            new IndentationRule("csharp_indent_block_contents", IndentationKind.BlockContents, ["true", "false"], 610),
            new IndentationRule("csharp_indent_braces", IndentationKind.Braces, ["true", "false"], 611),
            new IndentationRule("csharp_indent_case_contents_when_block", IndentationKind.CaseBlock, ["true", "false"], 612),
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
            new BracketSpacingRule(BracketSpacingKind.EmptyContents, 395),
            new BracketSpacingRule(BracketSpacingKind.Contents, 396),
            new SingleLineBlockPreservationRule(397),
            new SingleLineStatementPreservationRule(398),
            new SystemUsingSortRule(372),
            new ImportGroupSeparationRule(373),
            new ConstructLayoutRule("dress_arguments_layout", ConstructLayoutKind.Arguments, 400),
            new ConstructLayoutRule("dress_parameters_layout", ConstructLayoutKind.Parameters, 410),
            new ConstructLayoutRule("dress_initializers_layout", ConstructLayoutKind.Initializers, 420),
            new ConstructLayoutRule("dress_collection_expressions_layout", ConstructLayoutKind.CollectionExpressions, 430),
            new ConstructLayoutRule("dress_base_type_lists_layout", ConstructLayoutKind.BaseTypeLists, 440),
            new ConstructLayoutRule("dress_constraint_clauses_layout", ConstructLayoutKind.ConstraintClauses, 450),
            new ConstructLayoutRule("dress_member_access_chains_layout", ConstructLayoutKind.MemberAccessChains, 460),
            new ConstructLayoutRule("dress_binary_expressions_layout", ConstructLayoutKind.BinaryExpressions, 470),
            new ConstructLayoutRule("dress_conditional_expressions_layout", ConstructLayoutKind.ConditionalExpressions, 480),
            new ConstructLayoutRule("dress_query_clauses_layout", ConstructLayoutKind.QueryClauses, 490),
            new ConstructLayoutRule("dress_attributes_layout", ConstructLayoutKind.Attributes, 500)
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
