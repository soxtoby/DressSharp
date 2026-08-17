using Xunit;

namespace DressSharp.UnitTests;

public class MicrosoftCompatibilityRuleTests
{
    [Theory]
    [InlineData("all", "class C { void M() { } }", "M()\n{")]
    [InlineData("none", "class C\n{\n    void M()\n    {\n    }\n}", "M() {")]
    [InlineData("accessors", "class C { int P { get { return 1; } } }", "get\n{")]
    [InlineData("anonymous_methods", "class C { object M() => delegate() { }; }", "delegate()\n{")]
    [InlineData("anonymous_types", "class C { object M() => new { A = 1 }; }", "new\n{")]
    [InlineData("control_blocks", "class C { void M() { if (true) { } } }", "if (true)\n{")]
    [InlineData("events", "class C { event System.Action E { add { } remove { } } }", "E\n{")]
    [InlineData("indexers", "class C { int this[int x] { get => 1; } }", "]\n{")]
    [InlineData("lambdas", "class C { object M() => () => { }; }", "=>\n{")]
    [InlineData("local_functions", "class C { void M() { void L() { } } }", "L()\n{")]
    [InlineData("methods", "class C { void M() { } }", "M()\n{")]
    [InlineData("object_collection_array_initializers", "class C { int[] M() => new[] { 1 }; }", "[]\n{")]
    [InlineData("properties", "class C { int P { get; } }", "P\n{")]
    [InlineData("types", "class C { }", "C\n{")]
    public void Applies_open_brace_value(string value, string source, string expected)
    {
        var first = Transform(source, ("csharp_new_line_before_open_brace", value));
        Assert.Contains(expected, first.Replace("\r\n", "\n"));
        Assert.Equal(first, Transform(first, ("csharp_new_line_before_open_brace", value)));
    }

    [Theory]
    [InlineData("csharp_new_line_before_else", "class C { void M() { if (true) { } else { } } }", "}\nelse")]
    [InlineData("csharp_new_line_before_catch", "class C { void M() { try { } catch { } } }", "}\ncatch")]
    [InlineData("csharp_new_line_before_finally", "class C { void M() { try { } finally { } } }", "}\nfinally")]
    [InlineData("csharp_new_line_before_members_in_object_initializers", "class C { object M() => new C { P = 1, Q = 2 }; int P; int Q; }", ",\nQ")]
    [InlineData("csharp_new_line_before_members_in_anonymous_types", "class C { object M() => new { P = 1, Q = 2 }; }", ",\nQ")]
    [InlineData("csharp_new_line_between_query_expression_clauses", "class C { object M(int[] xs) => from x in xs where x > 0 select x; }", "xs\nwhere")]
    public void Applies_boolean_newline_rules(string key, string source, string expected)
    {
        var first = Transform(source, (key, "true"));
        Assert.Contains(expected, first.Replace("\r\n", "\n"));
        Assert.Equal(first, Transform(first, (key, "true")));
        Assert.DoesNotContain(expected, Transform(first, (key, "false")).Replace("\r\n", "\n"));
    }

    [Theory]
    [InlineData("csharp_indent_block_contents", "true", "class C\n{\nvoid M()\n{\nint x;\n}\n}", "\n        int x;")]
    [InlineData("csharp_indent_block_contents", "false", "class C\n{\nvoid M()\n{\n    int x;\n}\n}", "\nint x;")]
    [InlineData("csharp_indent_braces", "true", "class C\n{\nvoid M()\n{\n}\n}", "M()\n    {")]
    [InlineData("csharp_indent_braces", "false", "class C\n{\nvoid M()\n    {\n    }\n}", "M()\n{")]
    public void Applies_indentation_values(string key, string value, string source, string expected)
    {
        var first = Transform(source, (key, value));
        Assert.Contains(expected, first.Replace("\r\n", "\n"));
        Assert.Equal(first, Transform(first, (key, value)));
    }

    [Theory]
    [InlineData("csharp_new_line_before_else")]
    [InlineData("csharp_indent_block_contents")]
    public void Newline_and_indentation_missing_and_unset_preserve_source(string key)
    {
        const string source = "class C { void M() { if (true) { } else { } } }";
        Assert.Equal(source, Transform(source));
        Assert.Equal(source, Transform(source, (key, "unset")));
    }

    [Fact]
    public void Newline_and_indentation_rules_compose_and_are_idempotent()
    {
        const string source = "class C { void M(int x) { if (x > 0) { } else { switch (x) {\ncase 0:\nbreak;\n} } } }";
        (string, string)[] preferences =
        [
            ("csharp_new_line_before_open_brace", "all"),
            ("csharp_new_line_before_else", "true"),
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_braces", "false")
        ];
        var first = Transform(source, preferences);
        Assert.Equal(first, Transform(first, preferences));
    }

    [Fact]
    public void Newline_rules_preserve_comments_directives_malformed_regions_raw_strings_and_disabled_text()
    {
        const string source = "class C { void A() /* keep */ { }\n#if OFF\nvoid B() { }\n#endif\nstring S() => \"\"\"{ raw }\"\"\";\nvoid Broken( { }\nvoid Safe() { } }";
        var result = Transform(source, ("csharp_new_line_before_open_brace", "methods"));
        Assert.Contains("A() /* keep */ {", result);
        Assert.Contains("#if OFF\nvoid B() { }\n#endif", result);
        Assert.Contains("\"\"\"{ raw }\"\"\"", result);
        Assert.Contains("void Broken( {", result);
        Assert.Contains("Safe()\n{", result.Replace("\r\n", "\n"));
    }

    [Theory]
    [InlineData("csharp_space_after_cast", "true", "class C { int M(object x) => (int)x; }", "(int) x")]
    [InlineData("csharp_space_after_cast", "false", "class C { int M(object x) => (int) x; }", "(int)x")]
    [InlineData("csharp_space_after_keywords_in_control_flow_statements", "true", "class C { void M() { if(true) { } } }", "if (true)")]
    [InlineData("csharp_space_after_keywords_in_control_flow_statements", "false", "class C { void M() { if (true) { } } }", "if(true)")]
    [InlineData("csharp_space_before_colon_in_inheritance_clause", "false", "class C : B { } class B { }", "C: B")]
    [InlineData("csharp_space_after_colon_in_inheritance_clause", "false", "class C : B { } class B { }", "C :B")]
    [InlineData("csharp_space_around_binary_operators", "before_and_after", "class C { int M() => 1+2; }", "1 + 2")]
    [InlineData("csharp_space_around_binary_operators", "none", "class C { int M() => 1 + 2; }", "1+2")]
    [InlineData("csharp_space_around_binary_operators", "ignore", "class C { int M() => 1  +  2; }", "1  +  2")]
    [InlineData("csharp_space_between_method_declaration_parameter_list_parentheses", "true", "class C { void M(int x) { } }", "M( int x )")]
    [InlineData("csharp_space_between_method_declaration_empty_parameter_list_parentheses", "true", "class C { void M() { } }", "M( )")]
    [InlineData("csharp_space_between_method_declaration_name_and_open_parenthesis", "true", "class C { void M() { } }", "M ()")]
    [InlineData("csharp_space_between_method_call_parameter_list_parentheses", "true", "class C { void M() { N(1); } void N(int x) { } }", "N( 1 )")]
    [InlineData("csharp_space_between_method_call_empty_parameter_list_parentheses", "true", "class C { void M() { N(); } void N() { } }", "N( )")]
    [InlineData("csharp_space_between_method_call_name_and_opening_parenthesis", "true", "class C { void M() { N(); } void N() { } }", "N ()")]
    [InlineData("csharp_space_after_comma", "false", "class C { void M(int x, int y) { } }", "x,int")]
    [InlineData("csharp_space_before_comma", "true", "class C { void M(int x, int y) { } }", "x , int")]
    [InlineData("csharp_space_after_dot", "true", "class C { string M() => this.ToString(); }", "this. ToString")]
    [InlineData("csharp_space_before_dot", "true", "class C { string M() => this.ToString(); }", "this .ToString")]
    [InlineData("csharp_space_after_semicolon_in_for_statement", "false", "class C { void M() { for (int i = 0; i < 1; i++) { } } }", "0;i")]
    [InlineData("csharp_space_before_semicolon_in_for_statement", "true", "class C { void M() { for (int i = 0; i < 1; i++) { } } }", "0 ; i")]
    [InlineData("csharp_space_around_declaration_statements", "false", "class C { void M() { int x = 1; } }", "x=1")]
    [InlineData("csharp_space_before_open_square_brackets", "true", "class C { int M(int[] x) => x[0]; }", "x [0]")]
    [InlineData("csharp_space_between_square_brackets", "true", "class C { int M(int[] x) => x[0]; }", "x[ 0 ]")]
    public void Applies_spacing_value(string key, string value, string source, string expected)
    {
        var first = Transform(source, (key, value));
        Assert.Contains(expected, first);
        Assert.Equal(first, Transform(first, (key, value)));
    }

    [Theory]
    [InlineData("false", "( true )", "(true)")]
    [InlineData("control_flow_statements", "(true)", "( true )")]
    [InlineData("expressions", "(1 + 2)", "( 1 + 2 )")]
    [InlineData("type_casts", "(int)x", "( int )x")]
    public void Applies_parenthesis_categories(string value, string fragment, string expected)
    {
        var source = fragment.Contains("true") ? $"class C {{ void M() {{ if {fragment} {{ }} }} }}" : fragment.Contains("int") ? $"class C {{ int M(object x) => {fragment}; }}" : $"class C {{ int M() => {fragment}; }}";
        Assert.Contains(expected, Transform(source, ("csharp_space_between_parentheses", value)));
    }

    [Fact]
    public void Sorts_system_usings()
    {
        const string source = "using Zoo;\nusing System.Text;\nusing Alpha;\n";
        var sorted = Transform(source, ("dotnet_sort_system_directives_first", "true"));
        Assert.StartsWith("using System.Text;\nusing Zoo;", sorted);
    }

    [Theory]
    [InlineData("csharp_space_after_cast")]
    [InlineData("dotnet_sort_system_directives_first")]
    public void Missing_and_unset_preferences_preserve_source(string key)
    {
        const string source = "using Zoo;\nusing System;\nclass C { int M(object x) => (int) x; }";
        Assert.Equal(source, Transform(source));
        Assert.Equal(source, Transform(source, (key, "unset")));
    }

    [Fact]
    public void Comments_directives_and_malformed_occurrences_are_preserved()
    {
        const string source = "class C { int M() => 1 /* keep */  +  2;\n#if X\nint N() => 1  +  2;\n#endif\nint P() => (1 + ; }";
        var result = Transform(source, ("csharp_space_around_binary_operators", "none"));
        Assert.Contains("1 /* keep */  +  2", result);
        Assert.Contains("#if X\nint N() => 1  +  2;\n#endif", result);
        Assert.Contains("(1 + ;", result);
    }

    static string Transform(string source, params (string Key, string Value)[] preferences)
        => EmitterTestHarness.Format(source, preferences);
}
