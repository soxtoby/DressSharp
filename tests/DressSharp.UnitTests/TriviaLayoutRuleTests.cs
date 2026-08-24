using Xunit;

namespace DressSharp.UnitTests;

public sealed class TriviaLayoutRuleTests
{
    [Theory]
    [InlineData("dress_blank_lines_around_namespaces", "1", "using A;\nnamespace N { }", "using A;\n\nnamespace N")]
    [InlineData("dress_blank_lines_around_types", "1", "namespace N\n{\nclass A { }\nclass B { }\n}", "{\n\n    class A { }\n\n    class B")]
    [InlineData("dress_blank_lines_between_members", "0", "class C\n{\n    int A;\n\n\n    int B;\n}", "int A;\n    int B;")]
    [InlineData("dress_blank_lines_between_using_groups", "2", "using A;\nusing static B;", "using A;\n\n\nusing static B;")]
    [InlineData("dress_blank_lines_between_member_categories", "1", "class C\n{\n    int A;\n    int B;\n    void M() { }\n}", "int A;\n    int B;\n\n    void M()")]
    [InlineData("dress_max_consecutive_blank_lines", "0", "class C\n{\n\n\n    int A;\n}", "{\n    int A;")]
    public void Applies_each_blank_line_rule(
        string key,
        string value,
        string source,
        string expected)
    {
        var result = FormatTwice(source, (key, value));
        Assert.Contains(expected, result.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Around_namespaces_separates_both_sides_without_padding_file_boundaries()
    {
        const string source = "namespace First { }\nnamespace Second { }";
        const string expected = "namespace First { }\n\nnamespace Second { }";

        Assert.Equal(
            expected,
            FormatTwice(source, ("dress_blank_lines_around_namespaces", "1")));
    }

    [Fact]
    public void Around_namespaces_separates_nested_declarations_from_adjacent_syntax()
    {
        const string source = "namespace Outer\n{\n    namespace Inner { }\n    class C { }\n}";
        const string expected = "namespace Outer\n{\n\n    namespace Inner { }\n\n    class C { }\n}";

        Assert.Equal(
            expected,
            FormatTwice(source, ("dress_blank_lines_around_namespaces", "1")));
    }

    [Fact]
    public void Around_types_separates_both_sides_without_padding_file_boundaries()
    {
        const string source = "class A { }\nclass B { }";
        const string expected = "class A { }\n\nclass B { }";

        Assert.Equal(
            expected,
            FormatTwice(source, ("dress_blank_lines_around_types", "1")));
    }

    [Fact]
    public void Around_types_separates_nested_declarations_from_adjacent_members()
    {
        const string source = "class Outer\n{\n    int A;\n    class Nested { }\n    void M() { }\n}";
        const string expected = "class Outer\n{\n    int A;\n\n    class Nested { }\n\n    void M() { }\n}";

        Assert.Equal(
            expected,
            FormatTwice(source, ("dress_blank_lines_around_types", "1")));
    }

    [Fact]
    public void Around_types_keeps_leading_comments_attached_to_the_declaration()
    {
        const string source = "class A { }\n/// <summary>B</summary>\nclass B { }";
        const string expected = "class A { }\n\n/// <summary>B</summary>\nclass B { }";

        Assert.Equal(
            expected,
            FormatTwice(source, ("dress_blank_lines_around_types", "1")));
    }

    [Theory]
    [InlineData("0", "class C\n{\n    int A;\n    int B;\n}")]
    [InlineData("1", "class C\n{\n    int A;\n\n    int B;\n}")]
    [InlineData("10", "class C\n{\n    int A;\n\n\n\n\n\n\n\n\n\n\n    int B;\n}")]
    public void Blank_line_counts_accept_zero_and_multi_digit_values(string count, string expected)
    {
        const string source = "class C\n{\n    int A;\n    int B;\n}";
        Assert.Equal(expected, FormatTwice(source, ("dress_blank_lines_between_members", count)));
    }

    [Fact]
    public void Later_specific_boundary_rule_wins_then_maximum_caps_it()
    {
        const string source = "class C\n{\n    int A;\n    void M() { }\n}";
        var result = FormatTwice(
            source,
            ("dress_blank_lines_between_members", "0"),
            ("dress_blank_lines_between_member_categories", "3"),
            ("dress_max_consecutive_blank_lines", "1"));

        Assert.Contains("int A;\n\n    void M()", result);
    }

    [Fact]
    public void Blank_line_boundaries_do_not_cross_comments_or_directives()
    {
        const string source = "class C\n{\n    int A;\n    // attached\n    void M() { }\n#if X\n\n\n    int B;\n#endif\n}";
        var result = FormatTwice(
            source,
            ("dress_blank_lines_between_member_categories", "3"),
            ("dress_max_consecutive_blank_lines", "0"));

        Assert.Contains("int A;\n    // attached\n    void M()", result);
        Assert.Contains("#if X\n\n\n    int B;", result);
    }

    [Theory]
    [InlineData("dress_line_comment_spacing", "none", "class C { int A; // note\n}", "//note")]
    [InlineData("dress_line_comment_spacing", "single", "class C { int A; //note\n}", "// note")]
    [InlineData("dress_block_comment_spacing", "none", "class C { /* note */ int A; }", "/*note*/")]
    [InlineData("dress_block_comment_spacing", "single", "class C { /*note*/ int A; }", "/* note */")]
    [InlineData("dress_attached_comment_placement", "same_line", "class C { /* note */\n    int A; }", "/* note */ int A;")]
    [InlineData("dress_attached_comment_placement", "own_line", "class C { /* note */ int A; }", "{\n /* note */\n int A;")]
    [InlineData("dress_attached_comment_placement", "auto", "class C { /* note */  int A; }", "/* note */  int A;")]
    [InlineData("dress_xml_comment_placement", "separated", "/// <summary>Text</summary>\nclass C { }", "/// <summary>Text</summary>\n\nclass C")]
    [InlineData("dress_xml_comment_placement", "attached", "/// <summary>Text</summary>\n\nclass C { }", "/// <summary>Text</summary>\nclass C")]
    [InlineData("dress_xml_element_layout", "multi_line", "/// <summary>Text</summary>\nclass C { }", "/// <summary>\n/// Text\n/// </summary>")]
    [InlineData("dress_xml_element_layout", "single_line", "/// <summary>\n/// Text\n/// </summary>\nclass C { }", "/// <summary>Text</summary>")]
    public void Applies_every_comment_value(
        string key,
        string value,
        string source,
        string expected)
    {
        var result = FormatTwice(source, (key, value));
        Assert.Contains(expected, result.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Comment_rules_compose_in_catalog_order()
    {
        const string source = "class C { /*note*/ int A; //note\n}";
        var result = FormatTwice(
            source,
            ("dress_line_comment_spacing", "single"),
            ("dress_block_comment_spacing", "single"),
            ("dress_attached_comment_placement", "own_line"));

        Assert.Contains("{\n /* note */\n int A;\n // note\n}", result);
    }

    [Fact]
    public void Attached_placement_handles_every_comment_before_the_first_token()
    {
        const string source = "/* first */ /* second */ class C { }";
        var result = FormatTwice(
            source,
            ("dress_attached_comment_placement", "own_line"));

        Assert.Equal("/* first */\n /* second */\n class C { }", result);
    }

    [Fact]
    public void Own_line_isolates_a_trailing_comment_from_its_attached_syntax()
    {
        const string source = "class C { int A; /* note */\nint B; }";
        const string expected = "class C { int A;\n /* note */\nint B; }";

        Assert.Equal(
            expected,
            FormatTwice(source, ("dress_attached_comment_placement", "own_line")));
    }

    [Fact]
    public void Same_line_preserves_a_trailing_comment_boundary()
    {
        const string source = "class C { int A; /* note */\nint B; }";

        Assert.Equal(
            source,
            FormatTwice(source, ("dress_attached_comment_placement", "same_line")));
    }

    [Fact]
    public void Same_line_attaches_a_leading_block_comment_to_the_following_syntax()
    {
        const string source = "class C { int A;\n/* note */\nint B; }";
        const string expected = "class C { int A;\n/* note */ int B; }";

        Assert.Equal(
            expected,
            FormatTwice(source, ("dress_attached_comment_placement", "same_line")));
    }

    [Fact]
    public void Same_line_does_not_move_syntax_behind_a_line_comment()
    {
        const string source = "class C { int A;\n// note\nint B; }";

        Assert.Equal(
            source,
            FormatTwice(source, ("dress_attached_comment_placement", "same_line")));
    }

    [Fact]
    public void Same_line_does_not_move_a_following_directive_off_line_start()
    {
        const string source = "class C\n{\n/* note */\n#if X\nint A;\n#endif\n}";

        Assert.Equal(
            source,
            FormatTwice(source, ("dress_attached_comment_placement", "same_line")));
    }

    [Fact]
    public void Same_line_does_not_merge_adjacent_comments()
    {
        const string source = "class C\n{\n/* first */\n/* second */\nint A;\n}";
        const string expected = "class C\n{\n/* first */\n/* second */ int A;\n}";

        Assert.Equal(
            expected,
            FormatTwice(source, ("dress_attached_comment_placement", "same_line")));
    }

    [Fact]
    public void Attached_placement_handles_comments_without_surrounding_whitespace()
    {
        Assert.Equal(
            "class C { int A;\n/* note */\nint B; }",
            FormatTwice(
                "class C { int A;/* note */int B; }",
                ("dress_attached_comment_placement", "own_line")));
        Assert.Equal(
            "/* note */ class C { }",
            FormatTwice(
                "/* note */class C { }",
                ("dress_attached_comment_placement", "same_line")));
    }

    [Fact]
    public void Comment_rules_preserve_crlf_and_tab_indentation()
    {
        const string source = "class C\r\n{\r\n\t/*note*/\tint A;\r\n}\r\n";
        var result = FormatTwice(
            source,
            ("dress_block_comment_spacing", "single"),
            ("dress_attached_comment_placement", "own_line"));

        Assert.Contains("\t/* note */\r\n\tint A;", result);
        Assert.DoesNotContain("\n\tint A;", result.Replace("\r\n", ""));
    }

    [Fact]
    public void Xml_attached_placement_does_not_move_a_following_directive()
    {
        const string source = "/// <summary>Text</summary>\n\n#if X\nclass C { }\n#endif";

        Assert.Equal(
            source,
            FormatTwice(source, ("dress_xml_comment_placement", "attached")));
    }

    [Fact]
    public void Xml_attached_placement_keeps_block_documentation_separate_with_crlf()
    {
        const string source = "/** <summary>Text</summary> */\r\n\r\nclass C { }";
        const string expected = "/** <summary>Text</summary> */\r\nclass C { }";

        Assert.Equal(
            expected,
            FormatTwice(source, ("dress_xml_comment_placement", "attached")));
    }

    [Fact]
    public void Xml_separated_placement_adds_a_blank_line_after_block_documentation_with_crlf()
    {
        const string source = "/** <summary>Text</summary> */\r\nclass C { }";
        const string expected = "/** <summary>Text</summary> */\r\n\r\nclass C { }";

        Assert.Equal(
            expected,
            FormatTwice(source, ("dress_xml_comment_placement", "separated")));
    }

    [Fact]
    public void Multi_line_block_comment_content_is_not_reflowed()
    {
        const string source = "class C\n{\n    /*\n     * note\n     */\n    int A;\n}";
        Assert.Equal(source, FormatTwice(source, ("dress_block_comment_spacing", "single")));
    }

    [Fact]
    public void Xml_element_layout_preserves_text_content_whitespace_round_trip()
    {
        const string single = "/// <remarks>  Text  </remarks>\nclass C { }";
        const string multi = "/// <remarks>\n///   Text  \n/// </remarks>\nclass C { }";

        Assert.Equal(
            multi,
            FormatTwice(single, ("dress_xml_element_layout", "multi_line")));
        Assert.Equal(
            single,
            FormatTwice(multi, ("dress_xml_element_layout", "single_line")));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void Xml_element_layout_uses_the_source_line_ending(string lineEnding)
    {
        var source = $"/// <summary>Text</summary>{lineEnding}class C {{ }}";
        var expected =
            $"/// <summary>{lineEnding}/// Text{lineEnding}/// </summary>{lineEnding}class C {{ }}";

        Assert.Equal(
            expected,
            FormatTwice(source, ("dress_xml_element_layout", "multi_line")));
    }

    [Fact]
    public void Xml_element_layout_formats_each_historical_plain_text_element()
    {
        const string source = "/// <summary>One</summary>\n/// <custom-name>Two</custom-name>\nclass C { }";
        const string expected = "/// <summary>\n/// One\n/// </summary>\n/// <custom-name>\n/// Two\n/// </custom-name>\nclass C { }";

        Assert.Equal(
            expected,
            FormatTwice(source, ("dress_xml_element_layout", "multi_line")));
    }

    [Theory]
    [InlineData("/// <summary><see cref=\"C\"/></summary>\nclass C { }")]
    [InlineData("/// <summary attr=\"value\">Text</summary>\nclass C { }")]
    [InlineData("/// <summary />\nclass C { }")]
    [InlineData("/// <summary>Text</remarks>\nclass C { }")]
    [InlineData("/** <summary>Text</summary> */\nclass C { }")]
    public void Xml_element_layout_preserves_historically_ineligible_shapes(string source) =>
        Assert.Equal(
            source,
            FormatTwice(source, ("dress_xml_element_layout", "multi_line")));

    [Theory]
    [InlineData("dress_blank_lines_around_namespaces")]
    [InlineData("dress_blank_lines_around_types")]
    [InlineData("dress_blank_lines_between_members")]
    [InlineData("dress_blank_lines_between_using_groups")]
    [InlineData("dress_blank_lines_between_member_categories")]
    [InlineData("dress_max_consecutive_blank_lines")]
    [InlineData("dress_line_comment_spacing")]
    [InlineData("dress_block_comment_spacing")]
    [InlineData("dress_attached_comment_placement")]
    [InlineData("dress_xml_comment_placement")]
    [InlineData("dress_xml_element_layout")]
    public void Missing_and_unset_preferences_preserve_source(string key)
    {
        const string source = "/// <summary>Text</summary>\nclass C { /* note */ int A; // note\n}";
        Assert.Equal(source, EmitterTestHarness.Format(source));
        Assert.Equal(source, EmitterTestHarness.Format(source, (key, "unset")));
    }

    [Fact]
    public void Disabled_text_remains_byte_identical()
    {
        const string source = "#if false\nclass C\n{\n\n\n//note\n}\n#endif\nclass D { //note\n}";
        var result = FormatTwice(
            source,
            ("dress_max_consecutive_blank_lines", "0"),
            ("dress_line_comment_spacing", "single"));

        Assert.Contains("#if false\nclass C\n{\n\n\n//note\n}\n#endif", result);
        Assert.Contains("class D { // note", result);
    }

    [Fact]
    public void Malformed_target_is_skipped_while_safe_comment_is_formatted()
    {
        const string source = "class Broken\n{\n    int A;\n\n\n    void M( { }\n}\nclass Safe { //note\n}";
        var result = TransformTrivia(
            source,
            ("dress_blank_lines_between_member_categories", "0"),
            ("dress_line_comment_spacing", "single"));

        Assert.Contains("int A;\n\n\n    void M(", result);
        Assert.Contains("class Safe { // note", result);
    }

    static string TransformTrivia(
        string source,
        params (string Key, string Value)[] preferences)
    {
        var text = EmitterTestHarness.Format(source, preferences);
        Assert.Equal(text, EmitterTestHarness.Format(text, preferences));
        return text;
    }

    static string FormatTwice(
        string source,
        params (string Key, string Value)[] preferences)
    {
        var configured = preferences
            .Append(("csharp_indent_block_contents", "true"))
            .ToArray();
        var first = EmitterTestHarness.Format(source, configured);
        Assert.Equal(first, EmitterTestHarness.Format(first, configured));
        return first;
    }
}
