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
            new InitializerIndentationRule("dress_collection_expression_indentation", InitializerKind.CollectionExpression, 380),
            new MicrosoftSpacingRule("csharp_space_after_cast", 374),
            new MicrosoftSpacingRule("csharp_space_after_keywords_in_control_flow_statements", 375),
            new MicrosoftSpacingRule("csharp_space_between_parentheses", 376),
            new MicrosoftSpacingRule("csharp_space_before_colon_in_inheritance_clause", 377),
            new MicrosoftSpacingRule("csharp_space_after_colon_in_inheritance_clause", 378),
            new MicrosoftSpacingRule("csharp_space_around_binary_operators", 379),
            new MicrosoftSpacingRule("csharp_space_between_method_declaration_parameter_list_parentheses", 381),
            new MicrosoftSpacingRule("csharp_space_between_method_declaration_empty_parameter_list_parentheses", 382),
            new MicrosoftSpacingRule("csharp_space_between_method_declaration_name_and_open_parenthesis", 383),
            new MicrosoftSpacingRule("csharp_space_between_method_call_parameter_list_parentheses", 384),
            new MicrosoftSpacingRule("csharp_space_between_method_call_empty_parameter_list_parentheses", 385),
            new MicrosoftSpacingRule("csharp_space_between_method_call_name_and_opening_parenthesis", 386),
            new MicrosoftSpacingRule("csharp_space_after_comma", 387),
            new MicrosoftSpacingRule("csharp_space_before_comma", 388),
            new MicrosoftSpacingRule("csharp_space_after_dot", 389),
            new MicrosoftSpacingRule("csharp_space_before_dot", 390),
            new MicrosoftSpacingRule("csharp_space_after_semicolon_in_for_statement", 391),
            new MicrosoftSpacingRule("csharp_space_before_semicolon_in_for_statement", 392),
            new MicrosoftSpacingRule("csharp_space_around_declaration_statements", 393),
            new MicrosoftSpacingRule("csharp_space_before_open_square_brackets", 394),
            new MicrosoftSpacingRule("csharp_space_between_empty_square_brackets", 395),
            new MicrosoftSpacingRule("csharp_space_between_square_brackets", 396),
            new SingleLinePreservationRule("csharp_preserve_single_line_blocks", 397),
            new SingleLinePreservationRule("csharp_preserve_single_line_statements", 398),
            new MicrosoftUsingRule("dotnet_sort_system_directives_first", 372),
            new MicrosoftUsingRule("dotnet_separate_import_directive_groups", 373),
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
