using EasyAssertions;
using Xunit;

namespace DressSharp.UnitTests;

public sealed class DeclarationBoundaryRuleTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Multiline_parameter_close_supports_both_positions_without_changing_single_line_lists(string lineEnding)
    {
        const string source = "class C(int single)\n{\n    void M(int first, int second) { }\n}";
        var common = new[] { ("dress_parameters_layout", "always_multi") };

        var afterLast = Format(
            source.ReplaceLineEndings(lineEnding),
            [.. common, ("dress_parameters_closing_delimiter_position", "after_last_item")]);
        afterLast.ShouldBe("class C(\n    int single)\n{\n    void M(\n        int first,\n        int second) { }\n}".ReplaceLineEndings(lineEnding));

        var ownLine = Format(
            source.ReplaceLineEndings(lineEnding),
            [.. common, ("dress_parameters_closing_delimiter_position", "own_line")]);
        ownLine.ShouldBe("class C(\n    int single\n)\n{\n    void M(\n        int first,\n        int second\n    ) { }\n}".ReplaceLineEndings(lineEnding));

        Format(afterLast, [.. common, ("dress_parameters_closing_delimiter_position", "after_last_item")]).ShouldBe(afterLast);
        Format(ownLine, [.. common, ("dress_parameters_closing_delimiter_position", "own_line")]).ShouldBe(ownLine);

        Format("class C(int value) { }", ("dress_parameters_closing_delimiter_position", "own_line"))
            .ShouldBe("class C(int value) { }");
    }

    [Fact]
    public void Closing_parenthesis_position_applies_to_an_existing_multiline_list_without_owning_its_layout()
    {
        const string source = """
            class C
            {
                void M(
                    int first,
                    int second) { }
            }
            """;

        var result = Format(source, ("dress_parameters_closing_delimiter_position", "own_line"));
        result.ShouldBe("""
            class C
            {
                void M(
                    int first,
                    int second
                ) { }
            }
            """);
        Format(result, ("dress_parameters_closing_delimiter_position", "after_last_item"))
            .ShouldBe(source);
    }

    [Fact]
    public void Constructor_close_aligns_with_the_declaration_and_keeps_its_initializer_attached()
    {
        const string source = """
            abstract class TokenSpacingRule() : IFormattingRule
            {
                protected TokenSpacingRule(
                    RuleKey ruleKey,
                    string caption)
                    : this()
                {
                }
            }
            """;

        var result = Format(
            source,
            ("dress_parameters_closing_delimiter_position", "own_line"),
            ("csharp_indent_block_contents", "true"));

        result.ShouldBe("""
            abstract class TokenSpacingRule() : IFormattingRule
            {
                protected TokenSpacingRule(
                    RuleKey ruleKey,
                    string caption
                ) : this()
                {
                }
            }
            """);
        Format(
            result,
            ("dress_parameters_closing_delimiter_position", "own_line"),
            ("csharp_indent_block_contents", "true"))
            .ShouldBe(result);

        Format(
            source,
            ("dress_parameters_closing_delimiter_position", "after_last_item"),
            ("csharp_indent_block_contents", "true"))
            .ShouldBe("""
                abstract class TokenSpacingRule() : IFormattingRule
                {
                    protected TokenSpacingRule(
                        RuleKey ruleKey,
                        string caption) : this()
                    {
                    }
                }
                """);
    }

    [Theory]
    [InlineData("always_multi", 500)]
    [InlineData("auto", 20)]
    public void Wrapped_base_lists_keep_the_colon_with_the_first_base_type(string layout, int maximum)
    {
        const string source = "class LongClassName(int value) : TokenSpacingRule(value) { }";
        var result = Format(
            source,
            ("dress_base_type_lists_layout", layout),
            ("max_line_length", maximum.ToString()));

        result.ShouldBe("class LongClassName(int value)\n    : TokenSpacingRule(value) { }");
        Format(
            result,
            ("dress_base_type_lists_layout", layout),
            ("max_line_length", maximum.ToString()))
            .ShouldBe(result);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Auto_base_list_joins_a_fitting_base_type_to_an_own_line_parameter_close(string lineEnding)
    {
        const string source = "abstract class PunctuationSpacingRule(RuleKey ruleKey, string caption, string? subgroupName, SyntaxKind tokenKind, SpacingSide side, string defaultValue, string ownedSyntax)\n    : TokenSpacingRule(ruleKey, caption, subgroupName, [\"true\", \"false\"], defaultValue, ownedSyntax)\n{\n}";
        const string expected = "abstract class PunctuationSpacingRule(\n    RuleKey ruleKey,\n    string caption,\n    string? subgroupName,\n    SyntaxKind tokenKind,\n    SpacingSide side,\n    string defaultValue,\n    string ownedSyntax\n) : TokenSpacingRule(ruleKey, caption, subgroupName, [\"true\", \"false\"], defaultValue, ownedSyntax)\n{\n}";
        (string Key, string Value)[] preferences =
            [
                ("dress_parameters_layout", "auto"),
                ("dress_parameters_closing_delimiter_position", "own_line"),
                ("dress_base_type_lists_layout", "auto"),
                ("csharp_indent_block_contents", "true"),
                ("max_line_length", "160")
            ];

        var result = Format(source.ReplaceLineEndings(lineEnding), preferences);
        result.ShouldBe(expected.ReplaceLineEndings(lineEnding));
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Declaration_brace_attaches_only_directly_after_an_own_line_parameter_close()
    {
        const string source = """
            class Attached(string value)
            {
                Attached(
                    int number
                )
                {
                }

                Attached(
                    bool enabled
                ) : this("")
                {
                }

                void Method(
                    int number
                )
                {
                }

                int Value;
            }

            class Single(string value)
            {
                int Value;
            }

            class Derived(string value) : Base
            {
                int Value;
            }

            class Base
            {
            }
            """;
        (string Key, string Value)[] preferences =
            [
                ("dress_parameters_layout", "always_multi"),
                ("dress_parameters_closing_delimiter_position", "own_line"),
                ("dress_multiline_parameter_list_open_brace_position", "same_line"),
                ("dress_base_type_lists_layout", "auto"),
                ("csharp_indent_block_contents", "true"),
                ("csharp_new_line_before_open_brace", "all")
            ];

        var result = Format(source, preferences);
        result.ShouldBe("""
            class Attached(
                string value
            ) {
                Attached(
                    int number
                ) {
                }

                Attached(
                    bool enabled
                ) : this("")
                {
                }

                void Method(
                    int number
                ) {
                }

                int Value;
            }

            class Single(
                string value
            ) {
                int Value;
            }

            class Derived(
                string value
            ) : Base
            {
                int Value;
            }

            class Base
            {
            }
            """);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("same_line", ") {")]
    [InlineData("next_line", ")\n")]
    public void Parameter_list_brace_position_overrides_the_standard_declaration_brace_position(string position, string expectedBoundary)
    {
        const string source = "class C(\n    string value\n)\n{\n    C(\n        int value\n    )\n    {\n    }\n\n    void M(\n        int value\n    )\n    {\n    }\n}";
        var standard = position == "same_line" ? "all" : "none";

        var result = Format(
            source,
            ("dress_multiline_parameter_list_open_brace_position", position),
            ("csharp_new_line_before_open_brace", standard));

        (result.Split(expectedBoundary, StringSplitOptions.None).Length - 1).ShouldBe(3);
        Format(
            result,
            ("dress_multiline_parameter_list_open_brace_position", position),
            ("csharp_new_line_before_open_brace", standard))
            .ShouldBe(result);
    }

    static string Format(string source, params (string Key, string Value)[] preferences) =>
        EmitterTestHarness.Format(source, preferences);
}
