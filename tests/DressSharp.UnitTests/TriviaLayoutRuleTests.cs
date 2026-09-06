using Xunit;
using EasyAssertions;

namespace DressSharp.UnitTests;

public sealed class TriviaLayoutRuleTests
{
    [Theory]
    [InlineData("dress_blank_lines_around_namespaces", "1",
        """
        using A;
        namespace N { }
        """,
        """
        using A;

        namespace N { }
        """)]
    [InlineData("dress_blank_lines_around_types", "1",
        """
        namespace N
        {
        class A { }
        class B { }
        }
        """,
        """
        namespace N
        {
            class A { }

            class B { }
        }
        """)]
    [InlineData("dress_blank_lines_between_members", "0",
        """
        class C
        {
            int A;

            int B;
        }
        """,
        """
        class C
        {
            int A;
            int B;
        }
        """)]
    [InlineData("dress_blank_lines_between_using_groups", "2",
        """
        using A;
        using static B;
        """,
        """
        using A;


        using static B;
        """)]
    [InlineData("dress_blank_lines_between_member_categories", "1",
        """
        class C
        {
            int A;
            int B;
            void M() { }
        }
        """,
        """
        class C
        {
            int A;
            int B;

            void M() { }
        }
        """)]
    [InlineData("dress_max_consecutive_blank_lines", "0",
        """
        class C
        {

            int A;
        }
        """,
        """
        class C
        {
            int A;
        }
        """)]
    public void Applies_each_blank_line_rule(
        string key,
        string value,
        string source,
        string expected)
    {
        var result = FormatTwice(source, (key, value));
        result.ShouldBe(expected);
    }

    [Fact]
    public void Around_namespaces_separates_both_sides_without_padding_file_boundaries()
    {
        FormatTwice("""
            namespace First { }
            namespace Second { }
            """,
            ("dress_blank_lines_around_namespaces", "1")).ShouldBe("""
            namespace First { }

            namespace Second { }
            """);
    }

    [Fact]
    public void Around_namespaces_separates_nested_declarations_from_adjacent_syntax()
    {
        FormatTwice("""
            namespace Outer
            {
                namespace Inner { }
                class C { }
            }
            """,
            ("dress_blank_lines_around_namespaces", "1")).ShouldBe("""
            namespace Outer
            {

                namespace Inner { }

                class C { }
            }
            """);
    }

    [Fact]
    public void Around_types_separates_both_sides_without_padding_file_boundaries()
    {
        FormatTwice("""
            class A { }
            class B { }
            """,
            ("dress_blank_lines_around_types", "1")).ShouldBe("""
            class A { }

            class B { }
            """);
    }

    [Theory]
    [InlineData("class Outer")]
    [InlineData("namespace Outer")]
    public void Around_types_does_not_pad_containing_braces(string container)
    {
        var source = $$"""
            {{container}}
            {
                class Inner { }
            }
            """;

        FormatTwice(source, ("dress_blank_lines_around_types", "1")).ShouldBe(source);
    }

    [Fact]
    public void Around_types_separates_nested_declarations_from_adjacent_members()
    {
        FormatTwice("""
            class Outer
            {
                int A;
                class Nested { }
                void M() { }
            }
            """,
            ("dress_blank_lines_around_types", "1")).ShouldBe("""
            class Outer
            {
                int A;

                class Nested { }

                void M() { }
            }
            """);
    }

    [Fact]
    public void Around_types_keeps_leading_comments_attached_to_the_declaration()
    {
        FormatTwice("""
            class A { }
            /// <summary>B</summary>
            class B { }
            """,
            ("dress_blank_lines_around_types", "1")).ShouldBe("""
            class A { }

            /// <summary>B</summary>
            class B { }
            """);
    }

    [Theory]
    [InlineData("0", "class C\n{\n    int A;\n    int B;\n}")]
    [InlineData("1", "class C\n{\n    int A;\n\n    int B;\n}")]
    [InlineData("10", "class C\n{\n    int A;\n\n\n\n\n\n\n\n\n\n\n    int B;\n}")]
    public void Blank_line_counts_accept_zero_and_multi_digit_values(string count, string expected)
    {
        FormatTwice("""
            class C
            {
                int A;
                int B;
            }
            """,
            ("dress_blank_lines_between_members", count)).ShouldBe(expected);
    }

    [Fact]
    public void Later_specific_boundary_rule_wins_then_maximum_caps_it()
    {
        var result = FormatTwice(
            """
            class C
            {
                int A;
                void M() { }
            }
            """,
            ("dress_blank_lines_between_members", "0"),
            ("dress_blank_lines_between_member_categories", "3"),
            ("dress_max_consecutive_blank_lines", "1"));

        result.ShouldBe("""
            class C
            {
                int A;

                void M() { }
            }
            """);
    }

    [Fact]
    public void Blank_line_boundaries_do_not_cross_comments_or_directives()
    {
        var result = FormatTwice(
            """
            class C
            {
                int A;
                // attached
                void M() { }
            #if X


                int B;
            #endif
            }
            """,
            ("dress_blank_lines_between_member_categories", "3"),
            ("dress_max_consecutive_blank_lines", "0"));

        result.ShouldBe("""
            class C
            {
                int A;
                // attached
                void M() { }
            #if X


                int B;
            #endif
            }
            """);
    }

    [Theory]
    [InlineData("dress_line_comment_spacing",
        "none",
        """
        class C { int A; // note
        }
        """,
        """
        class C { int A; //note
        }
        """)]
    [InlineData("dress_line_comment_spacing",
        "single",
        """
        class C { int A; //note
        }
        """,
        """
        class C { int A; // note
        }
        """)]
    [InlineData("dress_block_comment_spacing", "none", "class C { /* note */ int A; }", "class C { /*note*/ int A; }")]
    [InlineData("dress_block_comment_spacing", "single", "class C { /*note*/ int A; }", "class C { /* note */ int A; }")]
    [InlineData("dress_attached_comment_placement",
        "same_line",
        """
        class C { /* note */
            int A; }
        """,
        "class C { /* note */ int A; }")]
    [InlineData("dress_attached_comment_placement",
        "own_line",
        "class C { /* note */ int A; }",
        """
        class C {
         /* note */
         int A; }
        """)]
    [InlineData("dress_attached_comment_placement", "auto", "class C { /* note */  int A; }", "class C { /* note */  int A; }")]
    [InlineData("dress_xml_comment_placement",
        "separated",
        """
        /// <summary>Text</summary>
        class C { }
        """,
        """
        /// <summary>Text</summary>

        class C { }
        """)]
    [InlineData("dress_xml_comment_placement",
        "attached",
        """
        /// <summary>Text</summary>

        class C { }
        """,
        """
        /// <summary>Text</summary>
        class C { }
        """)]
    [InlineData("dress_xml_element_layout",
        "multi_line",
        """
        /// <summary>Text</summary>
        class C { }
        """,
        """
        /// <summary>
        /// Text
        /// </summary>
        class C { }
        """)]
    [InlineData("dress_xml_element_layout",
        "single_line",
        """
        /// <summary>
        /// Text
        /// </summary>
        class C { }
        """,
        """
        /// <summary>Text</summary>
        class C { }
        """)]
    public void Applies_every_comment_value(
        string key,
        string value,
        string source,
        string expected)
    {
        var result = FormatTwice(source, (key, value));
        result.ShouldBe(expected);
    }

    [Fact]
    public void Comment_rules_compose_in_catalog_order()
    {
        var result = FormatTwice(
            """
            class C { /*note*/ int A; //note
            }
            """,
            ("dress_line_comment_spacing", "single"),
            ("dress_block_comment_spacing", "single"),
            ("dress_attached_comment_placement", "own_line"));

        result.ShouldBe("""
            class C {
             /* note */
             int A;
             // note
            }
            """);
    }

    [Fact]
    public void Attached_placement_handles_every_comment_before_the_first_token()
    {
        var result = FormatTwice(
            "/* first */ /* second */ class C { }",
            ("dress_attached_comment_placement", "own_line"));

        result.ShouldBe("/* first */\n /* second */\n class C { }");
    }

    [Fact]
    public void Own_line_isolates_a_trailing_comment_from_its_attached_syntax()
    {
        FormatTwice("""
            class C { int A; /* note */
            int B; }
            """,
            ("dress_attached_comment_placement", "own_line")).ShouldBe("""
            class C { int A;
             /* note */
            int B; }
            """);
    }

    [Fact]
    public void Same_line_preserves_a_trailing_comment_boundary()
    {
        FormatTwice("""
            class C { int A; /* note */
            int B; }
            """,
            ("dress_attached_comment_placement", "same_line")).ShouldBe("""
            class C { int A; /* note */
            int B; }
            """);
    }

    [Fact]
    public void Same_line_attaches_a_leading_block_comment_to_the_following_syntax()
    {
        FormatTwice("""
            class C { int A;
            /* note */
            int B; }
            """,
            ("dress_attached_comment_placement", "same_line")).ShouldBe("""
            class C { int A;
            /* note */ int B; }
            """);
    }

    [Fact]
    public void Same_line_does_not_move_syntax_behind_a_line_comment()
    {
        FormatTwice("""
            class C { int A;
            // note
            int B; }
            """,
            ("dress_attached_comment_placement", "same_line")).ShouldBe("""
            class C { int A;
            // note
            int B; }
            """);
    }

    [Fact]
    public void Same_line_does_not_move_a_following_directive_off_line_start()
    {
        FormatTwice("""
            class C
            {
            /* note */
            #if X
            int A;
            #endif
            }
            """,
            ("dress_attached_comment_placement", "same_line")).ShouldBe("""
            class C
            {
            /* note */
            #if X
            int A;
            #endif
            }
            """);
    }

    [Fact]
    public void Same_line_does_not_merge_adjacent_comments()
    {
        FormatTwice("""
            class C
            {
            /* first */
            /* second */
            int A;
            }
            """,
            ("dress_attached_comment_placement", "same_line")).ShouldBe("""
            class C
            {
            /* first */
            /* second */ int A;
            }
            """);
    }

    [Fact]
    public void Attached_placement_handles_comments_without_surrounding_whitespace()
    {
        FormatTwice(
            "class C { int A;/* note */int B; }",
            ("dress_attached_comment_placement", "own_line")).ShouldBe("class C { int A;\n/* note */\nint B; }");
        FormatTwice(
            "/* note */class C { }",
            ("dress_attached_comment_placement", "same_line")).ShouldBe("/* note */ class C { }");
    }

    [Fact]
    public void Comment_rules_preserve_crlf_and_tab_indentation()
    {
        const string sourceTemplate = """
            class C
            {
            	/*note*/	int A;
            }

            """;
        const string expectedTemplate = """
            class C
            {
            	/* note */
            	int A;
            }

            """;
        var source = sourceTemplate.Replace("\n", "\r\n");
        var expected = expectedTemplate.Replace("\n", "\r\n");
        var result = FormatTwice(
            source,
            ("dress_block_comment_spacing", "single"),
            ("dress_attached_comment_placement", "own_line"));

        result.ShouldBe(expected);
    }

    [Fact]
    public void Xml_attached_placement_does_not_move_a_following_directive()
    {
        FormatTwice("""
            /// <summary>Text</summary>

            #if X
            class C { }
            #endif
            """,
            ("dress_xml_comment_placement", "attached")).ShouldBe("""
            /// <summary>Text</summary>

            #if X
            class C { }
            #endif
            """);
    }

    [Fact]
    public void Xml_attached_placement_keeps_block_documentation_separate_with_crlf()
    {
        const string source = "/** <summary>Text</summary> */\r\n\r\nclass C { }";
        const string expected = "/** <summary>Text</summary> */\r\nclass C { }";

        FormatTwice(source, ("dress_xml_comment_placement", "attached")).ShouldBe(expected);
    }

    [Fact]
    public void Xml_separated_placement_adds_a_blank_line_after_block_documentation_with_crlf()
    {
        const string source = "/** <summary>Text</summary> */\r\nclass C { }";
        const string expected = "/** <summary>Text</summary> */\r\n\r\nclass C { }";

        FormatTwice(source, ("dress_xml_comment_placement", "separated")).ShouldBe(expected);
    }

    [Fact]
    public void Multi_line_block_comment_content_is_not_reflowed()
    {
        const string source = "class C\n{\n    /*\n     * note\n     */\n    int A;\n}";
        FormatTwice(source, ("dress_block_comment_spacing", "single")).ShouldBe(source);
    }

    [Fact]
    public void Xml_element_layout_preserves_text_content_whitespace_round_trip()
    {
        const string single = "/// <remarks>  Text  </remarks>\nclass C { }";
        const string multi = "/// <remarks>\n///   Text  \n/// </remarks>\nclass C { }";

        FormatTwice(single, ("dress_xml_element_layout", "multi_line")).ShouldBe(multi);
        FormatTwice(multi, ("dress_xml_element_layout", "single_line")).ShouldBe(single);
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

        FormatTwice(source, ("dress_xml_element_layout", "multi_line")).ShouldBe(expected);
    }

    [Fact]
    public void Xml_element_layout_formats_each_historical_plain_text_element()
    {
        const string source = "/// <summary>One</summary>\n/// <custom-name>Two</custom-name>\nclass C { }";
        const string expected = "/// <summary>\n/// One\n/// </summary>\n/// <custom-name>\n/// Two\n/// </custom-name>\nclass C { }";

        FormatTwice(source, ("dress_xml_element_layout", "multi_line")).ShouldBe(expected);
    }

    [Theory]
    [InlineData("/// <summary><see cref=\"C\"/></summary>\nclass C { }")]
    [InlineData("/// <summary attr=\"value\">Text</summary>\nclass C { }")]
    [InlineData("/// <summary />\nclass C { }")]
    [InlineData("/// <summary>Text</remarks>\nclass C { }")]
    [InlineData("/** <summary>Text</summary> */\nclass C { }")]
    public void Xml_element_layout_preserves_historically_ineligible_shapes(string source) =>
        FormatTwice(source, ("dress_xml_element_layout", "multi_line")).ShouldBe(source);

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
        EmitterTestHarness.Format(source).ShouldBe(source);
        EmitterTestHarness.Format(source, (key, "unset")).ShouldBe(source);
    }

    [Fact]
    public void Disabled_text_remains_byte_identical()
    {
        var result = FormatTwice(
            """
            #if false
            class C
            {


            //note
            }
            #endif
            class D { //note
            }
            """,
            ("dress_max_consecutive_blank_lines", "0"),
            ("dress_line_comment_spacing", "single"));

        result.ShouldBe("""
            #if false
            class C
            {


            //note
            }
            #endif
            class D { // note
            }
            """);
    }

    [Fact]
    public void Malformed_target_is_skipped_while_safe_comment_is_formatted()
    {
        var result = TransformTrivia(
            """
            class Broken
            {
                int A;


                void M( { }
            }
            class Safe { //note
            }
            """,
            ("dress_blank_lines_between_member_categories", "0"),
            ("dress_line_comment_spacing", "single"));

        result.ShouldBe("""
            class Broken
            {
                int A;


                void M( { }
            }
            class Safe { // note
            }
            """);
    }


    static string TransformTrivia(
        string source,
        params (string Key, string Value)[] preferences)
    {
        var text = EmitterTestHarness.Format(source, preferences);
        EmitterTestHarness.Format(text, preferences).ShouldBe(text);
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
        EmitterTestHarness.Format(first, configured).ShouldBe(first);
        return first;
    }
}
