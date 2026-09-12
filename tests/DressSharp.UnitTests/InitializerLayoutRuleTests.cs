using EasyAssertions;
using Xunit;
using static DressSharp.UnitTests.EmitterTestHarness;

namespace DressSharp.UnitTests;

public sealed class InitializerLayoutRuleTests
{
    [Fact]
    public void Expanded_collection_formats_interpolated_string_boundaries_without_changing_contents()
    {
        const string source = """var items = new[] { $"{ $"{value  +  1}" }", $"{other  +  2}" };""";
        const string expected = """
            var items = new[]
            {
                $"{ $"{value  +  1}" }",
                $"{other  +  2}"
            };
            """;
        (string, string)[] preferences =
        [
            ("dress_array_initializer_layout", "expanded"),
            ("dress_array_initializer_indentation", "not_indented"),
            ("csharp_indent_block_contents", "true"),
            ("csharp_space_around_binary_operators", "before_and_after")
        ];
        var result = Format(source, preferences);
        result.ShouldBe(expected);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Auto_collection_initializer_puts_each_interpolated_string_on_its_own_line(string lineEnding)
    {
        const string source = """
            void M()
            {
                var markdown = new List<string> { $"# Benchmark {timestamp:u}", "", $"Mode: {mode}  ", $"Corpus: {manifest["corpusHash"]}", "", "| Workers | DressSharp median | DressSharp p95 | dotnet format median | Ratio |", "| ---: | ---: | ---: | ---: | ---: |" };
            }
            """;
        const string expected = """
            void M()
            {
                var markdown = new List<string>
                    {
                        $"# Benchmark {timestamp:u}",
                        "",
                        $"Mode: {mode}  ",
                        $"Corpus: {manifest["corpusHash"]}",
                        "",
                        "| Workers | DressSharp median | DressSharp p95 | dotnet format median | Ratio |",
                        "| ---: | ---: | ---: | ---: | ---: |"
                    };
            }
            """;
        (string, string)[] preferences =
        [
            ("dress_collection_initializer_layout", "auto"),
            ("dress_collection_initializer_indentation", "indented"),
            ("csharp_indent_block_contents", "true"),
            ("max_line_length", "160"),
            ("end_of_line", lineEnding == "\n" ? "lf" : "crlf")
        ];
        var result = Format(source.ReplaceLineEndings(lineEnding), preferences);
        result.ShouldBe(expected.ReplaceLineEndings(lineEnding));
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData(
        "dress_object_initializer_layout",
        "dress_object_initializer_indentation",
        "C M() => new C { X = 1, Y = 2 };",
        """
        C M() => new C
        {
            X = 1,
            Y = 2
        };
        """)]
    [InlineData(
        "dress_collection_initializer_layout",
        "dress_collection_initializer_indentation",
        "List<int> M() => new() { 1, 2 };",
        """
        List<int> M() => new()
        {
            1,
            2
        };
        """)]
    [InlineData(
        "dress_array_initializer_layout",
        "dress_array_initializer_indentation",
        "int[] M() => new[] { 1, 2 };",
        """
        int[] M() => new[]
        {
            1,
            2
        };
        """)]
    [InlineData(
        "dress_with_initializer_layout",
        "dress_with_initializer_indentation",
        "C M(C value) => value with { X = 1, Y = 2 };",
        """
        C M(C value) => value with
        {
            X = 1,
            Y = 2
        };
        """)]
    public void Each_initializer_kind_has_an_independent_layout_rule(
        string layoutKey,
        string indentationKey,
        string compact,
        string expanded)
    {
        var preferences = new[]
        {
            (layoutKey, "expanded"),
            (indentationKey, "not_indented"),
            ("csharp_indent_block_contents", "true")
        };

        Format(compact, preferences).ShouldBe(expanded);
        Format(expanded, preferences).ShouldBe(expanded);
        Format(expanded,
            (layoutKey, "compact"),
            ("max_line_length", "200")).ShouldBe(compact);
    }

    [Fact]
    public void Auto_keeps_fitting_single_line_initializers_and_normalizes_multiline_initializers()
    {
        const string compact = "C M() => new C { X = 1, Y = 2 };";
        const string partial = """
            C M() => new C {
                X = 1, Y = 2 };
            """;
        const string expanded = """
            C M() => new C
            {
                X = 1,
                Y = 2
            };
            """;
        (string, string)[] preferences =
        [
            ("dress_object_initializer_layout", "auto"),
            ("dress_object_initializer_indentation", "not_indented"),
            ("csharp_indent_block_contents", "true"),
            ("max_line_length", "200")
        ];

        Format(compact, preferences).ShouldBe(compact);
        Format(partial, preferences).ShouldBe(expanded);
    }

    [Theory]
    [InlineData("compact", false)]
    [InlineData("auto", false)]
    [InlineData("expanded", true)]
    public void Object_initializer_layout_includes_anonymous_objects(string mode, bool expanded)
    {
        const string compact = "object M() => new { X = 1, Y = 2 };";
        const string multi = """
            object M() => new
            {
                X = 1,
                Y = 2
            };
            """;

        (string, string)[] preferences =
        [
            ("dress_object_initializer_layout", mode),
            ("dress_object_initializer_indentation", "not_indented"),
            ("csharp_indent_block_contents", "true"),
            ("csharp_new_line_before_open_brace", "anonymous_types"),
            ("csharp_new_line_before_members_in_anonymous_types", "true"),
            ("max_line_length", "200")
        ];
        var result = Format(compact, preferences);

        result.ShouldBe(expanded ? multi : compact);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Anonymous_object_auto_expands_when_too_wide()
    {
        Format(
                "object M() => new { FirstProperty = 1, SecondProperty = 2 };",
                ("dress_object_initializer_layout", "auto"),
                ("dress_object_initializer_indentation", "not_indented"),
                ("csharp_indent_block_contents", "true"),
                ("max_line_length", "40"))
            .ShouldBe("""
                object M() => new
                {
                    FirstProperty = 1,
                    SecondProperty = 2
                };
                """);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("compact")]
    public void Width_aware_modes_expand_when_an_item_contains_multiple_lines(string mode)
    {
        Format("""
                C M() => new C { X = N(
                    first,
                    second), Y = 2 };
                """,
                ("dress_object_initializer_layout", mode),
                ("dress_object_initializer_indentation", "not_indented"),
                ("csharp_indent_block_contents", "true"),
                ("max_line_length", "200"))
            .ShouldBe("""
                C M() => new C
                {
                    X = N(
                        first,
                        second),
                    Y = 2
                };
                """);
    }

    [Fact]
    public void Compact_and_auto_expand_when_the_single_line_form_exceeds_the_maximum()
    {
        const string source = "C M() => new C { FirstProperty = 1, SecondProperty = 2 };";
        const string expected = """
            C M() => new C
            {
                FirstProperty = 1,
                SecondProperty = 2
            };
            """;

        foreach (var mode in new[] { "compact", "auto" })
        {
            Format(source,
                    ("dress_object_initializer_layout", mode),
                    ("dress_object_initializer_indentation", "not_indented"),
                    ("csharp_indent_block_contents", "true"),
                    ("max_line_length", "40"))
                .ShouldBe(expected);
        }
    }

    [Fact]
    public void Initializer_layout_owns_braces_and_items_over_standard_newline_rules()
    {
        const string source = "C M() => new C { X = 1, Y = 2 };";
        var result = Format(source,
            ("dress_object_initializer_layout", "compact"),
            ("max_line_length", "200"),
            ("csharp_new_line_before_open_brace", "object_collection_array_initializers"),
            ("csharp_new_line_before_members_in_object_initializers", "true"));

        result.ShouldBe(source);
    }

    [Fact]
    public void Expanded_puts_empty_initializer_braces_on_separate_lines()
    {
        Format(
                "C M() => new C { };",
                ("dress_object_initializer_layout", "expanded"))
            .ShouldBe("""
                C M() => new C
                {
                };
                """);
    }

    [Fact]
    public void Configuring_one_initializer_kind_leaves_other_kinds_unchanged()
    {
        const string source = """
            class C
            {
                C Object() => new C { X = 1, Y = 2 };
                List<int> Collection() => new() { 1, 2 };
            }
            """;

        Format(source,
                ("dress_object_initializer_layout", "expanded"),
                ("dress_object_initializer_indentation", "not_indented"),
                ("csharp_indent_block_contents", "true"))
            .ShouldBe("""
                class C
                {
                    C Object() => new C
                    {
                        X = 1,
                        Y = 2
                    };
                    List<int> Collection() => new() { 1, 2 };
                }
                """);
    }

    [Fact]
    public void Nested_initializer_layouts_compose_and_stabilize()
    {
        (string, string)[] preferences =
        [
            ("dress_object_initializer_layout", "compact"),
            ("dress_collection_initializer_layout", "expanded"),
            ("dress_object_initializer_indentation", "not_indented"),
            ("dress_collection_initializer_indentation", "not_indented"),
            ("csharp_indent_block_contents", "true"),
            ("max_line_length", "200")
        ];

        var result = Format(
            "C M() => new C { Values = new List<int> { 1, 2 } };",
            preferences);

        result.ShouldBe("""
            C M() => new C
            {
                Values = new List<int>
                {
                    1,
                    2
                }
            };
            """);
        Format(result, preferences).ShouldBe(result);
    }

}
