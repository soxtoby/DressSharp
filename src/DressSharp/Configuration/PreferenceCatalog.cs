namespace DressSharp.Configuration;

static class PreferenceCatalog
{
    static readonly HashSet<string> BooleanValues = new(StringComparer.OrdinalIgnoreCase) { "true", "false" };

    internal static readonly IReadOnlyDictionary<string, string> Familiar = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["charset"] = "utf-8",
            ["end_of_line"] = "lf",
            ["insert_final_newline"] = "true",
            ["trim_trailing_whitespace"] = "true",
            ["indent_style"] = "space",
            ["indent_size"] = "4",
            ["tab_width"] = "4",
            ["max_line_length"] = "180",
            ["csharp_new_line_before_open_brace"] = "all",
            ["csharp_new_line_before_else"] = "true",
            ["csharp_new_line_before_catch"] = "true",
            ["csharp_new_line_before_finally"] = "true",
            ["csharp_new_line_before_members_in_object_initializers"] = "true",
            ["csharp_new_line_before_members_in_anonymous_types"] = "true",
            ["csharp_new_line_between_query_expression_clauses"] = "true",
            ["csharp_indent_case_contents"] = "true",
            ["csharp_indent_switch_labels"] = "true",
            ["csharp_indent_labels"] = "one_less_than_current",
            ["csharp_indent_block_contents"] = "true",
            ["csharp_indent_braces"] = "false",
            ["csharp_indent_case_contents_when_block"] = "true",
            ["csharp_space_after_cast"] = "false",
            ["csharp_space_after_keywords_in_control_flow_statements"] = "true",
            ["csharp_space_between_parentheses"] = "false",
            ["csharp_space_before_colon_in_inheritance_clause"] = "true",
            ["csharp_space_after_colon_in_inheritance_clause"] = "true",
            ["csharp_space_around_binary_operators"] = "before_and_after",
            ["csharp_space_between_method_declaration_parameter_list_parentheses"] = "false",
            ["csharp_space_between_method_declaration_empty_parameter_list_parentheses"] = "false",
            ["csharp_space_between_method_declaration_name_and_open_parenthesis"] = "false",
            ["csharp_space_between_method_call_parameter_list_parentheses"] = "false",
            ["csharp_space_between_method_call_empty_parameter_list_parentheses"] = "false",
            ["csharp_space_between_method_call_name_and_opening_parenthesis"] = "false",
            ["csharp_space_after_comma"] = "true",
            ["csharp_space_before_comma"] = "false",
            ["csharp_space_after_dot"] = "false",
            ["csharp_space_before_dot"] = "false",
            ["csharp_space_after_semicolon_in_for_statement"] = "true",
            ["csharp_space_before_semicolon_in_for_statement"] = "false",
            ["csharp_space_around_declaration_statements"] = "false",
            ["csharp_space_before_open_square_brackets"] = "false",
            ["csharp_space_between_empty_square_brackets"] = "false",
            ["csharp_space_between_square_brackets"] = "false",
            ["csharp_preserve_single_line_blocks"] = "true",
            ["csharp_preserve_single_line_statements"] = "true",
            ["dotnet_sort_system_directives_first"] = "true",
            ["dotnet_separate_import_directive_groups"] = "false",
            ["dress_arguments_layout"] = "auto",
            ["dress_parameters_layout"] = "auto",
            ["dress_initializers_layout"] = "auto",
            ["dress_collection_expressions_layout"] = "auto",
            ["dress_base_type_lists_layout"] = "auto",
            ["dress_constraint_clauses_layout"] = "auto",
            ["dress_member_access_chains_layout"] = "auto",
            ["dress_binary_expressions_layout"] = "auto",
            ["dress_conditional_expressions_layout"] = "auto",
            ["dress_query_clauses_layout"] = "auto",
            ["dress_attributes_layout"] = "auto",
            ["dress_blank_lines_around_namespaces"] = "1",
            ["dress_blank_lines_around_types"] = "1",
            ["dress_blank_lines_between_members"] = "1",
            ["dress_blank_lines_between_using_groups"] = "1",
            ["dress_blank_lines_between_member_categories"] = "1",
            ["dress_max_consecutive_blank_lines"] = "1",
            ["dress_line_comment_spacing"] = "single",
            ["dress_block_comment_spacing"] = "single",
            ["dress_attached_comment_placement"] = "auto",
            ["dress_xml_comment_placement"] = "attached",
            ["dress_xml_element_layout"] = "single_line",
            ["dress_global_using_order"] = "first",
            ["dress_using_kind_order"] = "ordinary,static,alias",
            ["csharp_preferred_modifier_order"] = "public,protected,internal,private,file,new,static,abstract,virtual,sealed,override,readonly,unsafe,required,volatile,async",
            ["dress_object_initializer_indentation"] = "indented",
            ["dress_collection_initializer_indentation"] = "indented",
            ["dress_array_initializer_indentation"] = "indented",
            ["dress_with_initializer_indentation"] = "indented",
            ["dress_collection_expression_indentation"] = "not_indented",
            ["dress_method_body"] = "expression",
            ["dress_constructor_body"] = "expression",
            ["dress_operator_body"] = "expression",
            ["dress_property_body"] = "expression",
            ["dress_indexer_body"] = "expression",
            ["dress_accessor_body"] = "expression",
            ["dress_lambda_body"] = "expression",
            ["dress_namespace_style"] = "file_scoped",
            ["dress_conditional_braces"] = "balanced"
        };

    internal static bool IsKnown(string key) => Familiar.ContainsKey(key);

    internal static bool IsValid(string key, string value)
    {
        value = NormalizeValue(key, value);
        if (value.Equals("unset", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!Familiar.TryGetValue(key, out var sample))
            return true;
        if (key.Equals("csharp_new_line_before_open_brace", StringComparison.OrdinalIgnoreCase))
            return value.Equals("all", StringComparison.OrdinalIgnoreCase) || value.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                IsSubset(value, "accessors anonymous_methods anonymous_types control_blocks events indexers lambdas local_functions methods object_collection_array_initializers properties types");
        if (key.Equals("csharp_space_between_parentheses", StringComparison.OrdinalIgnoreCase))
            return value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                IsSubset(value, "control_flow_statements expressions type_casts");
        if (BooleanValues.Contains(sample))
            return BooleanValues.Contains(value);
        if (key.Equals("indent_size", StringComparison.OrdinalIgnoreCase) && value.Equals("tab", StringComparison.OrdinalIgnoreCase))
            return true;
        if (key.Equals("max_line_length", StringComparison.OrdinalIgnoreCase) && value.Equals("off", StringComparison.OrdinalIgnoreCase))
            return true;
        if (int.TryParse(sample, out _))
        {
            var permitsZero = key.StartsWith("dress_blank_lines_", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("dress_max_consecutive_blank_lines", StringComparison.OrdinalIgnoreCase);
            return int.TryParse(value, out var number) && (permitsZero ? number >= 0 : number > 0);
        }

        if (key.Equals("dress_using_kind_order", StringComparison.OrdinalIgnoreCase))
            return IsPermutation(value, ["ordinary", "static", "alias"]);
        if (key.Equals("csharp_preferred_modifier_order", StringComparison.OrdinalIgnoreCase))
            return IsPermutation(value, sample.Split(','));
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
                "dress_line_comment_spacing" or "dress_block_comment_spacing" => "none single",
                "dress_attached_comment_placement" => "same_line own_line auto",
                "dress_xml_comment_placement" => "attached separated",
                "dress_namespace_style" => "file_scoped block_scoped",
                "dress_conditional_braces" => "compact always balanced",
                "dress_global_using_order" => "first last mixed",
                var k when k.EndsWith("_body", StringComparison.Ordinal) => "block expression",
                _ => sample
            };
        return allowed.Split(' ').Contains(value, StringComparer.OrdinalIgnoreCase);
    }

    internal static string Normalize(string key, string value) => NormalizeValue(key, value).ToLowerInvariant();

    static string NormalizeValue(string key, string value)
    {
        value = value.Trim();
        return key.StartsWith("csharp_", StringComparison.OrdinalIgnoreCase) || key.StartsWith("dotnet_", StringComparison.OrdinalIgnoreCase)
            ? WithoutSeverity(value)
            : value;
    }

    static string WithoutSeverity(string value)
    {
        var colon = value.IndexOf(':');
        return colon < 0 ? value : value[..colon].TrimEnd();
    }

    static bool IsPermutation(string value, IReadOnlyCollection<string> allowed)
    {
        var values = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return values.Length == allowed.Count 
            && values.Distinct(StringComparer.OrdinalIgnoreCase).Count() == allowed.Count 
            && values.All(item => allowed.Contains(item, StringComparer.OrdinalIgnoreCase));
    }

    static bool IsSubset(string value, string allowed)
    {
        var values = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var allowedValues = allowed.Split(' ');
        return values.Length > 0 && values.Distinct(StringComparer.OrdinalIgnoreCase).Count() == values.Length 
            && values.All(item => allowedValues.Contains(item, StringComparer.OrdinalIgnoreCase));
    }
}
