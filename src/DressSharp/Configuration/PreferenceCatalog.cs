namespace DressSharp.Configuration;

internal static class PreferenceCatalog
{
    private static readonly HashSet<string> BooleanValues = new(StringComparer.OrdinalIgnoreCase) { "true", "false" };

    internal static readonly IReadOnlyDictionary<string, string> Familiar = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["charset"] = "utf-8", ["end_of_line"] = "lf", ["insert_final_newline"] = "true",
        ["trim_trailing_whitespace"] = "true", ["indent_style"] = "space", ["indent_size"] = "4",
        ["tab_width"] = "4", ["dotnet_sort_system_directives_first"] = "true",
        ["dotnet_separate_import_directive_groups"] = "false",
        ["csharp_new_line_before_open_brace"] = "all",
        ["csharp_new_line_before_else"] = "true", ["csharp_new_line_before_catch"] = "true",
        ["csharp_new_line_before_finally"] = "true", ["csharp_new_line_before_members_in_object_initializers"] = "true",
        ["csharp_new_line_before_members_in_anonymous_types"] = "true",
        ["csharp_new_line_between_query_expression_clauses"] = "true",
        ["csharp_indent_block_contents"] = "true", ["csharp_indent_braces"] = "false",
        ["csharp_indent_case_contents"] = "true", ["csharp_indent_case_contents_when_block"] = "true",
        ["csharp_indent_labels"] = "one_less_than_current", ["csharp_indent_switch_labels"] = "true",
        ["csharp_space_after_cast"] = "false", ["csharp_space_after_colon_in_inheritance_clause"] = "true",
        ["csharp_space_after_comma"] = "true", ["csharp_space_after_dot"] = "false",
        ["csharp_space_after_keywords_in_control_flow_statements"] = "true",
        ["csharp_space_after_semicolon_in_for_statement"] = "true",
        ["csharp_space_around_binary_operators"] = "before_and_after",
        ["csharp_space_around_declaration_statements"] = "false",
        ["csharp_space_before_colon_in_inheritance_clause"] = "true", ["csharp_space_before_comma"] = "false",
        ["csharp_space_before_dot"] = "false", ["csharp_space_before_open_square_brackets"] = "false",
        ["csharp_space_before_semicolon_in_for_statement"] = "false", ["csharp_space_between_empty_square_brackets"] = "false",
        ["csharp_space_between_method_call_empty_parameter_list_parentheses"] = "false",
        ["csharp_space_between_method_call_name_and_opening_parenthesis"] = "false",
        ["csharp_space_between_method_call_parameter_list_parentheses"] = "false",
        ["csharp_space_between_method_declaration_empty_parameter_list_parentheses"] = "false",
        ["csharp_space_between_method_declaration_name_and_open_parenthesis"] = "false",
        ["csharp_space_between_method_declaration_parameter_list_parentheses"] = "false",
        ["csharp_space_between_parentheses"] = "false", ["csharp_space_between_square_brackets"] = "false",
        ["csharp_preserve_single_line_blocks"] = "true", ["csharp_preserve_single_line_statements"] = "true",
        ["dress_max_line_length"] = "180", ["dress_namespace_layout"] = "auto", ["dress_type_layout"] = "auto",
        ["dress_method_layout"] = "auto", ["dress_constructor_layout"] = "auto", ["dress_property_layout"] = "auto",
        ["dress_accessor_layout"] = "auto", ["dress_invocation_layout"] = "auto", ["dress_argument_layout"] = "auto",
        ["dress_parameter_layout"] = "auto", ["dress_collection_layout"] = "auto", ["dress_expression_layout"] = "auto",
        ["dress_namespace_blank_lines"] = "1", ["dress_type_blank_lines"] = "1", ["dress_member_blank_lines"] = "1",
        ["dress_member_category_blank_lines"] = "1", ["dress_using_group_blank_lines"] = "1",
        ["dress_max_blank_lines"] = "1", ["dress_comment_spacing"] = "single",
        ["dress_attached_comment_placement"] = "same_line_when_fit", ["dress_xml_element_layout"] = "single_line",
        ["dress_namespace_style"] = "file_scoped", ["dress_conditional_braces"] = "balanced",
        ["dress_expression_bodies"] = "when_possible", ["dress_using_order"] = "global_first_ordinary_static_alias",
        ["dress_modifier_order"] = "canonical", ["dress_object_initializer_indentation"] = "indented",
        ["dress_collection_initializer_indentation"] = "indented", ["dress_array_initializer_indentation"] = "indented",
        ["dress_with_initializer_indentation"] = "indented", ["dress_collection_expression_indentation"] = "not_indented"
    };

    internal static bool IsKnown(string key) => Familiar.ContainsKey(key);

    internal static bool IsValid(string key, string value)
    {
        value = NormalizeValue(key, value);
        if (value.Equals("unset", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!Familiar.TryGetValue(key, out var sample))
            return true;
        if (BooleanValues.Contains(sample))
            return BooleanValues.Contains(value);
        if (key.Equals("indent_size", StringComparison.OrdinalIgnoreCase) && value.Equals("tab", StringComparison.OrdinalIgnoreCase))
            return true;
        if (int.TryParse(sample, out _))
            return int.TryParse(value, out var number) && number >= 0;

        var allowed = key switch
        {
            "charset" => "latin1 utf-8 utf-8-bom utf-16be utf-16le",
            "end_of_line" => "cr lf crlf",
            "indent_style" => "space tab",
            "csharp_new_line_before_open_brace" => "all none accessors anonymous_methods anonymous_types control_blocks events indexers lambdas local_functions methods object_collection_array_initializers properties types",
            "csharp_indent_labels" => "flush_left no_change one_less_than_current",
            "csharp_space_around_binary_operators" => "before_and_after ignore none",
            "csharp_space_between_parentheses" => "control_flow_statements expressions type_casts false",
            var k when k.EndsWith("_layout", StringComparison.Ordinal) && k != "dress_xml_element_layout" => "always_single auto always_multi",
            var k when k.EndsWith("_indentation", StringComparison.Ordinal) => "indented not_indented",
            "dress_xml_element_layout" => "single_line multi_line",
            "dress_comment_spacing" => "single preserve",
            "dress_attached_comment_placement" => "same_line_when_fit own_line",
            "dress_namespace_style" => "file_scoped block_scoped",
            "dress_conditional_braces" => "compact always balanced",
            "dress_expression_bodies" => "when_possible never",
            "dress_using_order" => "global_first_ordinary_static_alias preserve",
            "dress_modifier_order" => "canonical preserve",
            _ => sample
        };
        return allowed.Split(' ').Contains(value, StringComparer.OrdinalIgnoreCase);
    }

    internal static string Normalize(string key, string value) => NormalizeValue(key, value).ToLowerInvariant();

    private static string NormalizeValue(string key, string value)
    {
        value = value.Trim();
        return key.StartsWith("csharp_", StringComparison.OrdinalIgnoreCase) || key.StartsWith("dotnet_", StringComparison.OrdinalIgnoreCase)
            ? WithoutSeverity(value)
            : value;
    }

    private static string WithoutSeverity(string value)
    {
        var colon = value.IndexOf(':');
        return colon < 0 ? value : value[..colon].TrimEnd();
    }
}
