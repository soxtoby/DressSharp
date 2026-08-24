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
    [InlineData("true", "class C { int[] x; int[,] y; }", "int[ ] x; int[,] y;")]
    [InlineData("false", "class C { int[ ] x; int[,] y; }", "int[] x; int[,] y;")]
    [InlineData("true", "class C { int[] [] x; }", "int[ ] [ ] x;")]
    [InlineData("false", "class C { int[ ] [ ] x; }", "int[] [] x;")]
    public void Applies_empty_square_bracket_spacing(string value, string source, string expected)
    {
        var first = Transform(source, ("csharp_space_between_empty_square_brackets", value));
        Assert.Contains(expected, first);
        Assert.Equal(first, Transform(first, ("csharp_space_between_empty_square_brackets", value)));
    }

    [Theory]
    [InlineData("true", "class C { void M() { int x = 1; } }", "{ int x = 1; }")]
    [InlineData("false", "class C { void M() { int x = 1; } }", "{\nint x = 1;\n}")]
    public void Applies_single_line_block_preservation(string value, string source, string expected)
    {
        var first = Transform(source, ("csharp_preserve_single_line_blocks", value));
        Assert.Contains(expected, first.Replace("\r\n", "\n"));
        Assert.Equal(first, Transform(first, ("csharp_preserve_single_line_blocks", value)));
    }

    [Fact]
    public void Expands_an_empty_single_line_block_without_a_blank_line()
    {
        const string source = "class C { void M() { } }";
        var first = Transform(source, ("csharp_preserve_single_line_blocks", "false"));
        Assert.Contains("M() {\n}", first.Replace("\r\n", "\n"));
        Assert.DoesNotContain("{\n\n}", first.Replace("\r\n", "\n"));
        Assert.Equal(first, Transform(first, ("csharp_preserve_single_line_blocks", "false")));
    }

    [Fact]
    public void Expands_a_single_line_property_accessor_list()
    {
        const string source = "class C { int Value { get; set; } }";

        var first = Transform(source, ("csharp_preserve_single_line_blocks", "false"));

        Assert.Contains("Value {\nget; set;\n}", first.Replace("\r\n", "\n"));
        Assert.Equal(first, Transform(first, ("csharp_preserve_single_line_blocks", "false")));
    }

    [Fact]
    public void Accessor_list_expansion_skips_comments_and_malformed_accessors()
    {
        const string source = "class C { int Commented { get; /* keep */ set; } int Broken { get; set } int Safe { get; set; } }";

        var first = Transform(source, ("csharp_preserve_single_line_blocks", "false"));

        Assert.Contains("Commented { get; /* keep */ set; }", first);
        Assert.Contains("Broken { get; set }", first);
        Assert.Contains("Safe {\nget; set;\n}", first.Replace("\r\n", "\n"));
        Assert.Equal(first, Transform(first, ("csharp_preserve_single_line_blocks", "false")));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    public void Block_expansion_uses_the_source_line_ending(string lineEnding)
    {
        var source = $"class C{lineEnding}{{{lineEnding}void Empty() {{ }}{lineEnding}void NonEmpty() {{ int x; }}{lineEnding}}}";
        var first = Transform(source, ("csharp_preserve_single_line_blocks", "false"));
        Assert.Contains($"Empty() {{{lineEnding}}}", first);
        Assert.Contains($"NonEmpty() {{{lineEnding}int x;{lineEnding}}}", first);
        Assert.Equal(first, Transform(first, ("csharp_preserve_single_line_blocks", "false")));
    }

    [Theory]
    [InlineData("true", "class C { void M() { int x = 1; int y = 2; } }", "int x = 1; int y")]
    [InlineData("false", "class C { void M() { int x = 1; int y = 2; } }", "int x = 1;\nint y")]
    public void Applies_single_line_statement_preservation(string value, string source, string expected)
    {
        var first = Transform(source, ("csharp_preserve_single_line_statements", value));
        Assert.Contains(expected, first.Replace("\r\n", "\n"));
        Assert.Equal(first, Transform(first, ("csharp_preserve_single_line_statements", value)));
    }

    [Fact]
    public void Separates_only_adjacent_statements_sharing_a_line_in_a_multiline_block()
    {
        const string source = "class C\n{\nvoid M()\n{\nint a = 1; int b = 2;\nint c = 3;\n}\n}";
        const string expected = "class C\n{\nvoid M()\n{\nint a = 1;\nint b = 2;\nint c = 3;\n}\n}";

        var first = Transform(source, ("csharp_preserve_single_line_statements", "false"));
        Assert.Equal(expected, first);
        Assert.Equal(first, Transform(first, ("csharp_preserve_single_line_statements", "false")));
    }

    [Fact]
    public void Separates_adjacent_statements_directly_under_a_switch_section()
    {
        const string source = "class C\n{\nvoid M(int value)\n{\nswitch (value)\n{\ncase 1: First(); Second(); break;\ndefault: Third(); Fourth(); break;\n}\n}\nvoid First() { }\nvoid Second() { }\nvoid Third() { }\nvoid Fourth() { }\n}";
        const string expected = "class C\n{\nvoid M(int value)\n{\nswitch (value)\n{\ncase 1: First();\nSecond();\nbreak;\ndefault: Third();\nFourth();\nbreak;\n}\n}\nvoid First() { }\nvoid Second() { }\nvoid Third() { }\nvoid Fourth() { }\n}";

        var first = Transform(source, ("csharp_preserve_single_line_statements", "false"));

        Assert.Equal(expected, first);
        Assert.Equal(first, Transform(first, ("csharp_preserve_single_line_statements", "false")));
    }

    [Fact]
    public void Switch_section_separation_skips_comments_directives_and_malformed_sections()
    {
        const string source = "class C\n{\nvoid M(int value)\n{\nswitch (value)\n{\ncase 0: First(); /* keep */ Second(); break;\ncase 1:\n#if X\nFirst(); Second();\n#endif\nbreak;\ncase 2: int broken = ; int valid = 2; break;\ndefault: Third(); Fourth(); break;\n}\n}\nvoid First() { }\nvoid Second() { }\nvoid Third() { }\nvoid Fourth() { }\n}";

        var first = Transform(source, ("csharp_preserve_single_line_statements", "false"));

        Assert.Contains("case 0: First(); /* keep */ Second(); break;", first);
        Assert.Contains("#if X\nFirst(); Second();\n#endif", first);
        Assert.Contains("case 2: int broken = ; int valid = 2; break;", first);
        Assert.Contains("default: Third();\nFourth();\nbreak;", first);
        Assert.Equal(first, Transform(first, ("csharp_preserve_single_line_statements", "false")));
    }

    [Fact]
    public void Statements_already_on_separate_lines_are_unchanged()
    {
        const string source = "class C\n{\nvoid M()\n{\nint a = 1;\nint b = 2;\n}\n}";
        Assert.Equal(source, Transform(source, ("csharp_preserve_single_line_statements", "false")));
    }

    [Fact]
    public void Separates_member_declarations_sharing_a_line()
    {
        const string source = "class C\n{\nint First; int Second;\n}";
        const string expected = "class C\n{\nint First;\nint Second;\n}";

        var first = Transform(source, ("csharp_preserve_single_line_statements", "false"));

        Assert.Equal(expected, first);
        Assert.Equal(first, Transform(first, ("csharp_preserve_single_line_statements", "false")));
    }

    [Fact]
    public void Member_separation_skips_comments_directives_and_malformed_members()
    {
        const string source = "class Commented\n{\nint First; /* keep */ int Second;\n}\nclass Directed\n{\n#if X\nint First; int Second;\n#endif\n}\nclass Broken\n{\nint First = ; int Second;\n}\nclass Safe\n{\nint First; int Second;\n}";

        var first = Transform(source, ("csharp_preserve_single_line_statements", "false"));

        Assert.Contains("int First; /* keep */ int Second;", first);
        Assert.Contains("#if X\nint First; int Second;\n#endif", first);
        Assert.Contains("int First = ; int Second;", first);
        Assert.Contains("class Safe\n{\nint First;\nint Second;\n}", first);
        Assert.Equal(first, Transform(first, ("csharp_preserve_single_line_statements", "false")));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    public void Statement_separation_uses_the_source_line_ending(string lineEnding)
    {
        var source = $"class C{lineEnding}{{{lineEnding}void M() {{ int a = 1; int b = 2; }}{lineEnding}}}";
        var first = Transform(source, ("csharp_preserve_single_line_statements", "false"));
        Assert.Contains($"int a = 1;{lineEnding}int b = 2;", first);
        Assert.Equal(first, Transform(first, ("csharp_preserve_single_line_statements", "false")));
    }

    [Fact]
    public void Preservation_rules_compose_with_block_indentation()
    {
        const string source = "class C\n{\nvoid Empty() { }\nvoid M() { int a = 1; int b = 2; }\n}";
        const string expected = "class C\n{\n    void Empty() {\n    }\n    void M() {\n        int a = 1;\n        int b = 2;\n    }\n}";
        (string, string)[] preferences =
        [
            ("csharp_preserve_single_line_blocks", "false"),
            ("csharp_preserve_single_line_statements", "false"),
            ("csharp_indent_block_contents", "true")
        ];

        var first = Transform(source, preferences);
        Assert.Equal(expected, first);
        Assert.Equal(first, Transform(first, preferences));
    }

    [Fact]
    public void Statement_separation_skips_unsafe_multiline_blocks_and_formats_a_safe_block()
    {
        const string source = "class C\n{\nvoid Commented()\n{\nint a = 1; /* keep */ int b = 2;\n}\nvoid Directed()\n{\n#if X\nint a = 1; int b = 2;\n#endif\n}\nvoid Broken()\n{\nint a = ; int b = 2;\n}\nvoid Safe()\n{\nint a = 1; int b = 2;\n}\n}";
        var first = Transform(source, ("csharp_preserve_single_line_statements", "false"));

        Assert.Contains("int a = 1; /* keep */ int b = 2;", first);
        Assert.Contains("#if X\nint a = 1; int b = 2;\n#endif", first);
        Assert.Contains("int a = ; int b = 2;", first);
        Assert.Contains("void Safe()\n{\nint a = 1;\nint b = 2;\n}", first);
        Assert.Equal(first, Transform(first, ("csharp_preserve_single_line_statements", "false")));
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
    [InlineData("true", "using System;\nusing Zoo;\n", "using System;\n\nusing Zoo;")]
    [InlineData("false", "using System;\n\n\nusing Zoo;\n", "using System;\nusing Zoo;")]
    public void Separates_import_directive_groups(string value, string source, string expected)
    {
        var first = Transform(source, ("dotnet_separate_import_directive_groups", value));
        Assert.Contains(expected, first.Replace("\r\n", "\n"));
        Assert.Equal(first, Transform(first, ("dotnet_separate_import_directive_groups", value)));
    }

    [Theory]
    [InlineData("true", "using System;\nusing Zoo;\nusing System.Text;\n", "using System;\n\nusing Zoo;\n\nusing System.Text;\n")]
    [InlineData("false", "using System;\n\n\nusing Zoo;\n\n\nusing System.Text;\n", "using System;\nusing Zoo;\nusing System.Text;\n")]
    public void Separates_each_alternating_import_group_boundary(string value, string source, string expected)
    {
        var first = Transform(source, ("dotnet_separate_import_directive_groups", value));

        Assert.Equal(expected, first);
        Assert.Equal(first, Transform(first, ("dotnet_separate_import_directive_groups", value)));
    }

    [Theory]
    [InlineData("csharp_space_after_cast")]
    [InlineData("csharp_space_between_empty_square_brackets")]
    [InlineData("csharp_preserve_single_line_blocks")]
    [InlineData("csharp_preserve_single_line_statements")]
    [InlineData("dotnet_sort_system_directives_first")]
    [InlineData("dotnet_separate_import_directive_groups")]
    public void Missing_and_unset_preferences_preserve_source(string key)
    {
        const string source = "using System;\n\nusing Zoo;\nclass C { void M() { int[ ] x; int y = (int) 1; } }";
        Assert.Equal(source, Transform(source));
        Assert.Equal(source, Transform(source, (key, "unset")));
    }

    [Fact]
    public void Compatibility_rules_compose_with_spacing_newlines_indentation_and_using_order()
    {
        const string source = "using Zoo;\nusing System.Text;\nusing Alpha;\nclass C { void M() { int[] x; int y = 1; } }";
        (string, string)[] preferences =
        [
            ("dotnet_sort_system_directives_first", "true"),
            ("dotnet_separate_import_directive_groups", "true"),
            ("csharp_space_between_empty_square_brackets", "true"),
            ("csharp_space_between_square_brackets", "true"),
            ("csharp_preserve_single_line_blocks", "false"),
            ("csharp_preserve_single_line_statements", "false"),
            ("csharp_new_line_before_open_brace", "all"),
            ("csharp_indent_block_contents", "true")
        ];

        var first = Transform(source, preferences).Replace("\r\n", "\n");
        Assert.StartsWith("using System.Text;\n\nusing Zoo;\nusing Alpha;", first);
        Assert.Contains("int[ ] x;\n        int y = 1;", first);
        Assert.Equal(first, Transform(first, preferences).Replace("\r\n", "\n"));
    }

    [Fact]
    public void Single_line_rules_skip_comments_directives_and_malformed_blocks_but_format_safe_blocks()
    {
        const string source = "class C { void Commented() { int a = 1; /* keep */ int b = 2; }\nvoid Directed() {\n#if X\nint a = 1; int b = 2;\n#endif\n}\nvoid Broken() { int a = ; int b = 2; }\nvoid Safe() { int a = 1; int b = 2; } }";
        var result = Transform(
            source,
            ("csharp_preserve_single_line_blocks", "false"),
            ("csharp_preserve_single_line_statements", "false")).Replace("\r\n", "\n");

        Assert.Contains("Commented() { int a = 1; /* keep */ int b = 2; }", result);
        Assert.Contains("int a = 1; int b = 2;\n#endif", result);
        Assert.Contains("Broken() { int a = ; int b = 2; }", result);
        Assert.Contains("Safe() {\nint a = 1;\nint b = 2;\n}", result);
    }

    [Fact]
    public void Empty_bracket_spacing_preserves_unsafe_boundary_and_formats_safe_occurrence()
    {
        const string source = "class C { int[/* keep */] Commented; int[ Broken; int[] Safe; }";
        var result = Transform(source, ("csharp_space_between_empty_square_brackets", "true"));
        Assert.Contains("int[/* keep */] Commented", result);
        Assert.Contains("int[ Broken", result);
        Assert.Contains("int[ ] Safe", result);
    }

    [Fact]
    public void Import_group_separation_preserves_comment_and_directive_boundaries()
    {
        const string comments = "using System;\n// keep\nusing Zoo;\n";
        const string directives = "using System;\n#if X\nusing Zoo;\n#endif\n";
        Assert.Equal(comments, Transform(comments, ("dotnet_separate_import_directive_groups", "true")));
        Assert.Equal(directives, Transform(directives, ("dotnet_separate_import_directive_groups", "true")));
    }

    [Fact]
    public void Import_group_separation_skips_a_list_containing_malformed_using()
    {
        const string source = "using System;\nusing ;\nusing Zoo;\n";
        Assert.Equal(source, Transform(source, ("dotnet_separate_import_directive_groups", "true")));
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
