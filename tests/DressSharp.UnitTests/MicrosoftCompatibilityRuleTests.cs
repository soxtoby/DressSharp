using Xunit;
using EasyAssertions;
using static DressSharp.UnitTests.EmitterTestHarness;

namespace DressSharp.UnitTests;

public class MicrosoftCompatibilityRuleTests
{
    [Theory]
    [InlineData("all",
        "class C { void M() { } }",
        """
        class C
        { void M()
        { } }
        """)]
    [
        InlineData("none",
        """
        class C
        {
            void M()
            {
            }
        }
        """,
        """
        class C {
            void M() {
            }
        }
        """)
    ]
    [InlineData("accessors",
        "class C { int P { get { return 1; } } }",
        """
        class C { int P { get
        { return 1; } } }
        """)]
    [InlineData("anonymous_methods",
        "class C { object M() => delegate() { }; }",
        """
        class C { object M() => delegate()
        { }; }
        """)]
    [InlineData("anonymous_types",
        "class C { object M() => new { A = 1 }; }",
        """
        class C { object M() => new
        { A = 1 }; }
        """)]
    [InlineData("control_blocks",
        "class C { void M() { if (true) { } } }",
        """
        class C { void M() { if (true)
        { } } }
        """)]
    [
        InlineData("events",
        "class C { event System.Action E { add { } remove { } } }",
        """
        class C { event System.Action E
        { add { } remove { } } }
        """)
    ]
    [InlineData("indexers",
        "class C { int this[int x] { get => 1; } }",
        """
        class C { int this[int x]
        { get => 1; } }
        """)]
    [InlineData("lambdas",
        "class C { object M() => () => { }; }",
        """
        class C { object M() => () =>
        { }; }
        """)]
    [InlineData("local_functions",
        "class C { void M() { void L() { } } }",
        """
        class C { void M() { void L()
        { } } }
        """)]
    [InlineData("methods",
        "class C { void M() { } }",
        """
        class C { void M()
        { } }
        """)]
    [
        InlineData("object_collection_array_initializers",
        "class C { int[] M() => new[] { 1 }; }",
        """
        class C { int[] M() => new[]
        { 1 }; }
        """)
    ]
    [InlineData("properties",
        "class C { int P { get; } }",
        """
        class C { int P
        { get; } }
        """)]
    [InlineData("types",
        "class C { }",
        """
        class C
        { }
        """)]
    public void Applies_open_brace_value(string value, string source, string expected)
    {
        var first = Format(source, ("csharp_new_line_before_open_brace", value));
        first.ShouldBe(expected);
        Format(first, ("csharp_new_line_before_open_brace", value)).ShouldBe(first);
    }

    [Fact]
    public void Single_line_block_preservation_wins_over_open_brace_newlines()
    {
        Format(
            """
                class C
                {
                    int P { get; }
                    void M() { Run(); }
                    void Run() { }
                }
                """,
            ("csharp_new_line_before_open_brace", "all"),
            ("csharp_preserve_single_line_blocks", "true"))
            .ShouldBe("""
                class C
                {
                    int P { get; }
                    void M() { Run(); }
                    void Run() { }
                }
                """);
    }

    [Theory]
    [
        InlineData("csharp_new_line_before_else",
        "class C { void M() { if (true) { } else { } } }",
        """
        class C { void M() { if (true) { }
        else { } } }
        """)
    ]
    [
        InlineData("csharp_new_line_before_catch",
        "class C { void M() { try { } catch { } } }",
        """
        class C { void M() { try { }
        catch { } } }
        """)
    ]
    [
        InlineData("csharp_new_line_before_finally",
        "class C { void M() { try { } finally { } } }",
        """
        class C { void M() { try { }
        finally { } } }
        """)
    ]
    [
        InlineData("csharp_new_line_before_members_in_object_initializers",
        "class C { object M() => new C { P = 1, Q = 2 }; int P; int Q; }",
        """
        class C { object M() => new C {
        P = 1,
        Q = 2
        }; int P; int Q; }
        """)
    ]
    [
        InlineData("csharp_new_line_before_members_in_anonymous_types",
        "class C { object M() => new { P = 1, Q = 2 }; }",
        """
        class C { object M() => new {
        P = 1,
        Q = 2
        }; }
        """)
    ]
    [
        InlineData("csharp_new_line_between_query_expression_clauses",
        "class C { object M(int[] xs) => from x in xs where x > 0 select x; }",
        """
        class C { object M(int[] xs) => from x in xs
        where x > 0
        select x; }
        """)
    ]
    public void Applies_boolean_newline_rules(string key, string source, string expected)
    {
        var first = Format(source, (key, "true"));
        first.ShouldBe(expected);
        Format(first, (key, "true")).ShouldBe(first);
        Format(first, (key, "false")).ShouldBe(source);
    }

    [Theory]
    [
        InlineData("csharp_indent_block_contents",
        "true",
        """
        class C
        {
        void M()
        {
        int x;
        }
        }
        """,
        """
        class C
        {
            void M()
            {
                int x;
            }
        }
        """)
    ]
    [
        InlineData("csharp_indent_block_contents",
        "false",
        """
        class C
        {
        void M()
        {
            int x;
        }
        }
        """,
        """
        class C
        {
            void M()
            {
            int x;
            }
        }
        """)
    ]
    [
        InlineData("csharp_indent_braces",
        "true",
        """
        class C
        {
        void M()
        {
        }
        }
        """,
        """
        class C
            {
        void M()
            {
            }
            }
        """)
    ]
    [
        InlineData("csharp_indent_braces",
        "false",
        """
        class C
        {
        void M()
            {
            }
        }
        """,
        """
        class C
        {
        void M()
        {
        }
        }
        """)
    ]
    public void Applies_indentation_values(string key, string value, string source, string expected)
    {
        var first = Format(source, (key, value));
        first.ShouldBe(expected);
        Format(first, (key, value)).ShouldBe(first);
    }

    [Theory]
    [InlineData("csharp_new_line_before_else")]
    [InlineData("csharp_indent_block_contents")]
    public void Newline_and_indentation_missing_and_unset_preserve_source(string key)
    {
        Format("class C { void M() { if (true) { } else { } } }")
            .ShouldBe("class C { void M() { if (true) { } else { } } }");
        Format(
            "class C { void M() { if (true) { } else { } } }",
            (key, "unset"))
            .ShouldBe("class C { void M() { if (true) { } else { } } }");
    }

    [Fact]
    public void Newline_and_indentation_rules_compose_and_are_idempotent()
    {
        (string, string)[] preferences =
            [
                ("csharp_new_line_before_open_brace", "all"),
                ("csharp_new_line_before_else", "true"),
                ("csharp_indent_block_contents", "true"),
                ("csharp_indent_braces", "false")
            ];
        var first = Format(
            """
            class C { void M(int x) { if (x > 0) { } else { switch (x) {
            case 0:
            break;
            } } } }
            """,
            preferences);
        Format(first, preferences).ShouldBe(first);
    }

    [Fact]
    public void Newline_rules_preserve_comments_directives_malformed_regions_raw_strings_and_disabled_text()
    {
        Format(
            """"
                class C { void A() /* keep */ { }
                #if OFF
                void B() { }
                #endif
                string S() => """{ raw }""";
                void Broken( { }
                void Safe() { } }
                """",
            ("csharp_new_line_before_open_brace", "methods"))
            .ShouldBe(""""
                class C { void A() /* keep */ { }
                #if OFF
                void B() { }
                #endif
                string S() => """{ raw }""";
                void Broken( { }
                void Safe()
                { } }
                """");
    }

    [Theory]
    [InlineData("csharp_space_after_cast", "true", "class C { int M(object x) => (int)x; }", "class C { int M(object x) => (int) x; }")]
    [InlineData("csharp_space_after_cast", "false", "class C { int M(object x) => (int) x; }", "class C { int M(object x) => (int)x; }")]
    [
        InlineData("csharp_space_after_keywords_in_control_flow_statements", "true", "class C { void M() { if(true) { } } }", "class C { void M() { if (true) { } } }")
    ]
    [
        InlineData("csharp_space_after_keywords_in_control_flow_statements", "false", "class C { void M() { if (true) { } } }", "class C { void M() { if(true) { } } }")
    ]
    [InlineData("csharp_space_before_colon_in_inheritance_clause", "false", "class C : B { } class B { }", "class C: B { } class B { }")]
    [InlineData("csharp_space_after_colon_in_inheritance_clause", "false", "class C : B { } class B { }", "class C :B { } class B { }")]
    [InlineData("csharp_space_around_binary_operators", "before_and_after", "class C { int M() => 1+2; }", "class C { int M() => 1 + 2; }")]
    [InlineData("csharp_space_around_binary_operators", "none", "class C { int M() => 1 + 2; }", "class C { int M() => 1+2; }")]
    [InlineData("csharp_space_around_binary_operators", "ignore", "class C { int M() => 1  +  2; }", "class C { int M() => 1  +  2; }")]
    [
        InlineData("csharp_space_between_method_declaration_parameter_list_parentheses", "true", "class C { void M(int x) { } }", "class C { void M( int x ) { } }")
    ]
    [InlineData("csharp_space_between_method_declaration_empty_parameter_list_parentheses", "true", "class C { void M() { } }", "class C { void M( ) { } }")]
    [InlineData("csharp_space_between_method_declaration_name_and_open_parenthesis", "true", "class C { void M() { } }", "class C { void M () { } }")]
    [
        InlineData("csharp_space_between_method_declaration_name_and_open_parenthesis",
        "false",
        "class C { void M() { _occurrences.Sort(static (left, right) => 0); } }",
        "class C { void M() { _occurrences.Sort(static (left, right) => 0); } }")
    ]
    [
        InlineData("csharp_space_between_method_call_parameter_list_parentheses",
        "true",
        "class C { void M() { N(1); } void N(int x) { } }",
        "class C { void M() { N( 1 ); } void N(int x) { } }")
    ]
    [
        InlineData("csharp_space_between_method_call_empty_parameter_list_parentheses",
        "true",
        "class C { void M() { N(); } void N() { } }",
        "class C { void M() { N( ); } void N() { } }")
    ]
    [
        InlineData("csharp_space_between_method_call_name_and_opening_parenthesis",
        "true",
        "class C { void M() { N(); } void N() { } }",
        "class C { void M() { N (); } void N() { } }")
    ]
    [InlineData("csharp_space_after_comma", "false", "class C { void M(int x, int y) { } }", "class C { void M(int x,int y) { } }")]
    [InlineData("csharp_space_before_comma", "true", "class C { void M(int x, int y) { } }", "class C { void M(int x , int y) { } }")]
    [InlineData("csharp_space_after_dot", "true", "class C { string M() => this.ToString(); }", "class C { string M() => this. ToString(); }")]
    [InlineData("csharp_space_before_dot", "true", "class C { string M() => this.ToString(); }", "class C { string M() => this .ToString(); }")]
    [
        InlineData("dress_space_after_collection_spread_operator", "true", "class C { int[] M(int[] values) => [..values]; }", "class C { int[] M(int[] values) => [.. values]; }")
    ]
    [
        InlineData("dress_space_after_collection_spread_operator", "false", "class C { int[] M(int[] values) => [.. values]; }", "class C { int[] M(int[] values) => [..values]; }")
    ]
    [
        InlineData("csharp_space_after_semicolon_in_for_statement",
        "false",
        "class C { void M() { for (int i = 0; i < 1; i++) { } } }",
        "class C { void M() { for (int i = 0;i < 1;i++) { } } }")
    ]
    [
        InlineData("csharp_space_before_semicolon_in_for_statement",
        "true",
        "class C { void M() { for (int i = 0; i < 1; i++) { } } }",
        "class C { void M() { for (int i = 0 ; i < 1 ; i++) { } } }")
    ]
    [InlineData("csharp_space_around_declaration_statements", "false", "class C { void M() { int x  =  1; } }", "class C { void M() { int x = 1; } }")]
    [InlineData("csharp_space_before_open_square_brackets", "true", "class C { int M(int[] x) => x[0]; }", "class C { int M(int [] x) => x [0]; }")]
    [InlineData("csharp_space_between_square_brackets", "true", "class C { int M(int[] x) => x[0]; }", "class C { int M(int[] x) => x[ 0 ]; }")]
    public void Applies_spacing_value(string key, string value, string source, string expected)
    {
        var first = Format(source, (key, value));
        first.ShouldBe(expected);
        Format(first, (key, value)).ShouldBe(first);
    }

    [Fact]
    public void Declaration_spacing_ignore_preserves_existing_spaces()
    {
        Format(
            "class C { void M() { int x  =  1; } }",
            ("csharp_space_around_declaration_statements", "ignore"))
            .ShouldBe("class C { void M() { int x  =  1; } }");
    }

    [Fact]
    public void Collection_spread_spacing_preserves_unset_comments_line_breaks_and_ranges()
    {
        const string source = "class C { int[] M(int[] values) => [..  values]; }";
        Format(source).ShouldBe(source);
        Format(source, ("dress_space_after_collection_spread_operator", "unset")).ShouldBe(source);

        const string boundaries = "class C { int[] M(int[] values) => [../* keep */ values]; int[] N(int[] values, int end) => values[..end]; }";
        Format(boundaries, ("dress_space_after_collection_spread_operator", "true")).ShouldBe(boundaries);

        const string lineBreak = "class C { int[] M(int[] values) => [..\nvalues]; }";
        Format(lineBreak, ("dress_space_after_collection_spread_operator", "true")).ShouldBe(lineBreak);
    }

    [Theory]
    [InlineData("true", "class C { int[] x; int[,] y; }", "class C { int[ ] x; int[,] y; }")]
    [InlineData("false", "class C { int[ ] x; int[,] y; }", "class C { int[] x; int[,] y; }")]
    [InlineData("true", "class C { int[] [] x; }", "class C { int[ ] [ ] x; }")]
    [InlineData("false", "class C { int[ ] [ ] x; }", "class C { int[] [] x; }")]
    public void Applies_empty_square_bracket_spacing(string value, string source, string expected)
    {
        var first = Format(source, ("csharp_space_between_empty_square_brackets", value));
        first.ShouldBe(expected);
        Format(first, ("csharp_space_between_empty_square_brackets", value)).ShouldBe(first);
    }

    [Theory]
    [InlineData("true",
        "class C { void M() { int x = 1; } }",
        "class C { void M() { int x = 1; } }")]
    [InlineData("false",
        "class C { void M() { int x = 1; } }",
        """
        class C {
        void M() {
        int x = 1;
        }
        }
        """)]
    public void Applies_single_line_block_preservation(string value, string source, string expected)
    {
        var first = Format(source, ("csharp_preserve_single_line_blocks", value));
        first.ShouldBe(expected);
        Format(first, ("csharp_preserve_single_line_blocks", value)).ShouldBe(first);
    }

    [Fact]
    public void Expands_an_empty_single_line_block_without_a_blank_line()
    {
        var first = Format(
            "class C { void M() { } }",
            ("csharp_preserve_single_line_blocks", "false"));
        first.ShouldBe("""
            class C {
            void M() {
            }
            }
            """);
        Format(first, ("csharp_preserve_single_line_blocks", "false")).ShouldBe(first);
    }

    [Fact]
    public void Expands_a_single_line_property_accessor_list()
    {
        var first = Format(
            "class C { int Value { get; set; } }",
            ("csharp_preserve_single_line_blocks", "false"));
        first.ShouldBe("""
            class C {
            int Value {
            get; set;
            }
            }
            """);
        Format(first, ("csharp_preserve_single_line_blocks", "false")).ShouldBe(first);
    }

    [Fact]
    public void Preserving_trivial_blocks_keeps_empty_blocks_and_auto_accessor_lists_while_expanding_the_rest()
    {
        (string, string)[] preferences =
            [
                ("csharp_preserve_single_line_blocks", "false"),
                ("csharp_preserve_single_line_statements", "false"),
                ("dress_preserve_trivial_single_line_blocks", "true")
            ];
        var first = Format("""
            namespace Empty { }
            class Marker { }
            class C
            {
            bool Ready { get; } = true;
            int Auto { get; private set; }
            int Field { get => field; set => field = value; }
            int Mixed { get; set { field = value; } }
            void Empty() { }
            void Body() { Work(); Work(); }
            void Braced(bool go) { if (go) Work(); else { Work(); return; } }
            }
            """, preferences);
        first.ShouldBe("""
            namespace Empty { }
            class Marker { }
            class C
            {
            bool Ready { get; } = true;
            int Auto { get; private set; }
            int Field {
            get => field; set => field = value;
            }
            int Mixed {
            get; set {
            field = value;
            }
            }
            void Empty() { }
            void Body() {
            Work();
            Work();
            }
            void Braced(bool go) {
            if (go) Work(); else {
            Work();
            return;
            }
            }
            }
            """);
        Format(first, preferences).ShouldBe(first);
    }

    [Fact]
    public void Preserved_trivial_blocks_keep_their_opening_brace_on_the_same_line()
    {
        (string, string)[] preferences =
            [
                ("csharp_preserve_single_line_blocks", "false"),
                ("csharp_preserve_single_line_statements", "false"),
                ("dress_preserve_trivial_single_line_blocks", "true"),
                ("csharp_new_line_before_open_brace", "all"),
                ("csharp_new_line_before_else", "true"),
                ("csharp_indent_block_contents", "true"),
                ("indent_style", "space"),
                ("indent_size", "4")
            ];
        var first = Format("""
            class C
            {
                bool Ready { get; } = true;
                void Empty() { }
                void Body(bool go) { if (go) { Work(); } else { Work(); return; } }
            }
            """, preferences);
        first.ShouldBe("""
            class C
            {
                bool Ready { get; } = true;
                void Empty() { }
                void Body(bool go)
                {
                    if (go)
                    {
                        Work();
                    }
                    else
                    {
                        Work();
                        return;
                    }
                }
            }
            """);
        Format(first, preferences).ShouldBe(first);
    }

    [Fact]
    public void Preserving_trivial_blocks_does_nothing_while_blocks_are_preserved()
    {
        const string source = "class C { bool Ready { get; } void Empty() { } void Body() { Work(); } }";
        Format(
            source,
            ("csharp_preserve_single_line_blocks", "true"),
            ("dress_preserve_trivial_single_line_blocks", "true"))
            .ShouldBe(source);
    }

    [Fact]
    public void Accessor_list_expansion_skips_comments_and_malformed_accessors()
    {
        var first = Format(
            "class C { int Commented { get; /* keep */ set; } int Broken { get; set } int Safe { get; set; } }",
            ("csharp_preserve_single_line_blocks", "false"));
        first.ShouldBe("""
            class C { int Commented { get; /* keep */ set; } int Broken { get; set } int Safe {
            get; set;
            } }
            """);
        Format(first, ("csharp_preserve_single_line_blocks", "false")).ShouldBe(first);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    public void Block_expansion_uses_the_source_line_ending(string lineEnding)
    {
        var first = Format(
            """
                class C
                {
                void Empty() { }
                void NonEmpty() { int x; }
                }
                """.ReplaceLineEndings(lineEnding),
            ("csharp_preserve_single_line_blocks", "false"));
        first.ShouldBe("""
            class C
            {
            void Empty() {
            }
            void NonEmpty() {
            int x;
            }
            }
            """.ReplaceLineEndings(lineEnding));
        Format(first, ("csharp_preserve_single_line_blocks", "false")).ShouldBe(first);
    }

    [Theory]
    [InlineData("true",
        "class C { void M() { int x = 1; int y = 2; } }",
        "class C { void M() { int x = 1; int y = 2; } }")]
    [InlineData("false",
        "class C { void M() { int x = 1; int y = 2; } }",
        """
        class C { void M() { int x = 1;
        int y = 2; } }
        """)]
    public void Applies_single_line_statement_preservation(string value, string source, string expected)
    {
        var first = Format(source, ("csharp_preserve_single_line_statements", value));
        first.ShouldBe(expected);
        Format(first, ("csharp_preserve_single_line_statements", value)).ShouldBe(first);
    }

    [Fact]
    public void Separates_only_adjacent_statements_sharing_a_line_in_a_multiline_block()
    {
        var first = Format(
            """
            class C
            {
            void M()
            {
            int a = 1; int b = 2;
            int c = 3;
            }
            }
            """,
            ("csharp_preserve_single_line_statements", "false"));
        first.ShouldBe("""
            class C
            {
            void M()
            {
            int a = 1;
            int b = 2;
            int c = 3;
            }
            }
            """);
        Format(first, ("csharp_preserve_single_line_statements", "false")).ShouldBe(first);
    }

    [Fact]
    public void Separates_adjacent_statements_directly_under_a_switch_section()
    {
        var first = Format(
            """
            class C
            {
            void M(int value)
            {
            switch (value)
            {
            case 1: First(); Second(); break;
            default: Third(); Fourth(); break;
            }
            }
            void First() { }
            void Second() { }
            void Third() { }
            void Fourth() { }
            }
            """,
            ("csharp_preserve_single_line_statements", "false"));

        first.ShouldBe("""
            class C
            {
            void M(int value)
            {
            switch (value)
            {
            case 1: First();
            Second();
            break;
            default: Third();
            Fourth();
            break;
            }
            }
            void First() { }
            void Second() { }
            void Third() { }
            void Fourth() { }
            }
            """);
        Format(first, ("csharp_preserve_single_line_statements", "false")).ShouldBe(first);
    }

    [Fact]
    public void Switch_section_separation_skips_comments_directives_and_malformed_sections()
    {
        var first = Format(
            """
            class C
            {
            void M(int value)
            {
            switch (value)
            {
            case 0: First(); /* keep */ Second(); break;
            case 1:
            #if X
            First(); Second();
            #endif
            break;
            case 2: int broken = ; int valid = 2; break;
            default: Third(); Fourth(); break;
            }
            }
            void First() { }
            void Second() { }
            void Third() { }
            void Fourth() { }
            }
            """,
            ("csharp_preserve_single_line_statements", "false"));

        first.ShouldBe("""
            class C
            {
            void M(int value)
            {
            switch (value)
            {
            case 0: First(); /* keep */ Second(); break;
            case 1:
            #if X
            First(); Second();
            #endif
            break;
            case 2: int broken = ; int valid = 2; break;
            default: Third();
            Fourth();
            break;
            }
            }
            void First() { }
            void Second() { }
            void Third() { }
            void Fourth() { }
            }
            """);
        Format(first, ("csharp_preserve_single_line_statements", "false")).ShouldBe(first);
    }

    [Fact]
    public void Statements_already_on_separate_lines_are_unchanged()
    {
        Format(
            """
            class C
            {
            void M()
            {
            int a = 1;
            int b = 2;
            }
            }
            """,
            ("csharp_preserve_single_line_statements", "false")).ShouldBe("""
            class C
            {
            void M()
            {
            int a = 1;
            int b = 2;
            }
            }
            """);
    }

    [Fact]
    public void Separates_member_declarations_sharing_a_line()
    {
        var first = Format(
            """
            class C
            {
            int First; int Second;
            }
            """,
            ("csharp_preserve_single_line_statements", "false"));

        first.ShouldBe("""
            class C
            {
            int First;
            int Second;
            }
            """);
        Format(first, ("csharp_preserve_single_line_statements", "false")).ShouldBe(first);
    }

    [Fact]
    public void Member_separation_skips_comments_directives_and_malformed_members()
    {
        var first = Format(
            """
            class Commented
            {
            int First; /* keep */ int Second;
            }
            class Directed
            {
            #if X
            int First; int Second;
            #endif
            }
            class Broken
            {
            int First = ; int Second;
            }
            class Safe
            {
            int First; int Second;
            }
            """,
            ("csharp_preserve_single_line_statements", "false"));

        first.ShouldBe("""
            class Commented
            {
            int First; /* keep */ int Second;
            }
            class Directed
            {
            #if X
            int First; int Second;
            #endif
            }
            class Broken
            {
            int First = ; int Second;
            }
            class Safe
            {
            int First;
            int Second;
            }
            """);
        Format(first, ("csharp_preserve_single_line_statements", "false")).ShouldBe(first);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    public void Statement_separation_uses_the_source_line_ending(string lineEnding)
    {
        var first = Format(
            """
                class C
                {
                void M() { int a = 1; int b = 2; }
                }
                """.ReplaceLineEndings(lineEnding),
            ("csharp_preserve_single_line_statements", "false"));
        first.ShouldBe("""
            class C
            {
            void M() { int a = 1;
            int b = 2; }
            }
            """.ReplaceLineEndings(lineEnding));
        Format(first, ("csharp_preserve_single_line_statements", "false")).ShouldBe(first);
    }

    [Fact]
    public void Preservation_rules_compose_with_block_indentation()
    {
        (string, string)[] preferences =
            [
                ("csharp_preserve_single_line_blocks", "false"),
                ("csharp_preserve_single_line_statements", "false"),
                ("csharp_indent_block_contents", "true")
            ];

        var first = Format(
            """
            class C
            {
            void Empty() { }
            void M() { int a = 1; int b = 2; }
            }
            """,
            preferences);
        first.ShouldBe("""
            class C
            {
                void Empty() {
                }
                void M() {
                    int a = 1;
                    int b = 2;
                }
            }
            """);
        Format(first, preferences).ShouldBe(first);
    }

    [Fact]
    public void Statement_separation_skips_unsafe_multiline_blocks_and_formats_a_safe_block()
    {
        var first = Format(
            """
            class C
            {
            void Commented()
            {
            int a = 1; /* keep */ int b = 2;
            }
            void Directed()
            {
            #if X
            int a = 1; int b = 2;
            #endif
            }
            void Broken()
            {
            int a = ; int b = 2;
            }
            void Safe()
            {
            int a = 1; int b = 2;
            }
            }
            """,
            ("csharp_preserve_single_line_statements", "false"));

        first.ShouldBe("""
            class C
            {
            void Commented()
            {
            int a = 1; /* keep */ int b = 2;
            }
            void Directed()
            {
            #if X
            int a = 1; int b = 2;
            #endif
            }
            void Broken()
            {
            int a = ; int b = 2;
            }
            void Safe()
            {
            int a = 1;
            int b = 2;
            }
            }
            """);
        Format(first, ("csharp_preserve_single_line_statements", "false")).ShouldBe(first);
    }

    [Theory]
    [InlineData("false", "class C { void M() { if ( true ) { } } }", "class C { void M() { if (true) { } } }")]
    [InlineData("control_flow_statements", "class C { void M() { if (true) { } } }", "class C { void M() { if ( true ) { } } }")]
    [InlineData("expressions", "class C { int M() => (1 + 2); }", "class C { int M() => ( 1 + 2 ); }")]
    [InlineData("type_casts", "class C { int M(object x) => (int)x; }", "class C { int M(object x) => ( int )x; }")]
    public void Applies_parenthesis_categories(string value, string source, string expected)
    {
        Format(source, ("csharp_space_between_parentheses", value)).ShouldBe(expected);
    }

    [Fact]
    public void Sorts_system_usings()
    {
        Format(
            """
                using Zoo;
                using System.Text;
                using Alpha;

                """,
            ("dotnet_sort_system_directives_first", "true"))
            .ShouldBe("""
                using System.Text;
                using Zoo;
                using Alpha;

                """);
    }

    [Theory]
    [InlineData("true",
        """
        using System;
        using Zoo;

        """,
        """
        using System;

        using Zoo;

        """)]
    [InlineData("false",
        """
        using System;


        using Zoo;

        """,
        """
        using System;
        using Zoo;

        """)]
    public void Separates_import_directive_groups(string value, string source, string expected)
    {
        var first = Format(source, ("dotnet_separate_import_directive_groups", value));
        first.ShouldBe(expected);
        Format(first, ("dotnet_separate_import_directive_groups", value)).ShouldBe(first);
    }

    [Theory]
    [
        InlineData("true",
        """
        using System;
        using Zoo;
        using System.Text;

        """,
        """
        using System;

        using Zoo;

        using System.Text;

        """)
    ]
    [
        InlineData("false",
        """
        using System;


        using Zoo;


        using System.Text;

        """,
        """
        using System;
        using Zoo;
        using System.Text;

        """)
    ]
    public void Separates_each_alternating_import_group_boundary(string value, string source, string expected)
    {
        var first = Format(source, ("dotnet_separate_import_directive_groups", value));

        first.ShouldBe(expected);
        Format(first, ("dotnet_separate_import_directive_groups", value)).ShouldBe(first);
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
        Format("""
                using System;

                using Zoo;
                class C { void M() { int[ ] x; int y = (int) 1; } }
                """)
            .ShouldBe("""
                using System;

                using Zoo;
                class C { void M() { int[ ] x; int y = (int) 1; } }
                """);
        Format(
            """
                using System;

                using Zoo;
                class C { void M() { int[ ] x; int y = (int) 1; } }
                """,
            (key, "unset"))
            .ShouldBe("""
                using System;

                using Zoo;
                class C { void M() { int[ ] x; int y = (int) 1; } }
                """);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Compatibility_rules_compose_with_spacing_newlines_indentation_and_using_order(string newline)
    {
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

        var first = Format(
            """
            using Zoo;
            using System.Text;
            using Alpha;
            class C { void M() { int[] x; int y = 1; } }
            """.ReplaceLineEndings(newline),
            preferences);
        first.ShouldBe("""
            using System.Text;

            using Zoo;
            using Alpha;
            class C
            {
                void M()
                {
                    int[ ] x;
                    int y = 1;
                }
            }
            """.ReplaceLineEndings(newline));
        Format(first, preferences).ShouldBe(first);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Single_line_rules_skip_comments_directives_and_malformed_blocks_but_format_safe_blocks(string newline)
    {
        Format(
            """
                class C { void Commented() { int a = 1; /* keep */ int b = 2; }
                void Directed() {
                #if X
                int a = 1; int b = 2;
                #endif
                }
                void Broken() { int a = ; int b = 2; }
                void Safe() { int a = 1; int b = 2; } }
                """.ReplaceLineEndings(newline),
            ("csharp_preserve_single_line_blocks", "false"),
            ("csharp_preserve_single_line_blocks", "false"),
            ("csharp_preserve_single_line_statements", "false"))
            .ShouldBe("""
                class C { void Commented() { int a = 1; /* keep */ int b = 2; }
                void Directed() {
                #if X
                int a = 1; int b = 2;
                #endif
                }
                void Broken() { int a = ; int b = 2; }
                void Safe() {
                int a = 1;
                int b = 2;
                } }
                """.ReplaceLineEndings(newline));
    }

    [Fact]
    public void Empty_bracket_spacing_preserves_unsafe_boundary_and_formats_safe_occurrence()
    {
        Format(
            "class C { int[/* keep */] Commented; int[ Broken; int[] Safe; }",
            ("csharp_space_between_empty_square_brackets", "true"))
            .ShouldBe("class C { int[/* keep */] Commented; int[ Broken; int[ ] Safe; }");
    }

    [Fact]
    public void Import_group_separation_preserves_comment_and_directive_boundaries()
    {
        const string comments = "using System;\n// keep\nusing Zoo;\n";
        const string directives = "using System;\n#if X\nusing Zoo;\n#endif\n";
        Format(comments, ("dotnet_separate_import_directive_groups", "true")).ShouldBe(comments);
        Format(directives, ("dotnet_separate_import_directive_groups", "true")).ShouldBe(directives);
    }

    [Fact]
    public void Import_group_separation_skips_a_list_containing_malformed_using()
    {
        Format(
            """
                using System;
                using ;
                using Zoo;

                """,
            ("dotnet_separate_import_directive_groups", "true"))
            .ShouldBe("""
                using System;
                using ;
                using Zoo;

                """);
    }

    [Fact]
    public void Comments_directives_and_malformed_occurrences_are_preserved()
    {
        Format(
            """
                class C { int M() => 1 /* keep */  +  2;
                #if X
                int N() => 1  +  2;
                #endif
                int P() => (1 + ; }
                """,
            ("csharp_space_around_binary_operators", "none"))
            .ShouldBe("""
                class C { int M() => 1 /* keep */  +  2;
                #if X
                int N() => 1  +  2;
                #endif
                int P() => (1 + ; }
                """);
    }
}
