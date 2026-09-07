using EasyAssertions;
using Xunit;
using static DressSharp.UnitTests.EmitterTestHarness;

namespace DressSharp.UnitTests;

public sealed class SwitchExpressionIndentationEmitterTests
{
    [Fact]
    public void Indents_switch_expression_braces_and_arms()
    {
        (string, string)[] preferences =
        [
            ("dress_switch_expression_indentation", "indented"),
            ("csharp_indent_braces", "false"),
            ("csharp_indent_block_contents", "true")
        ];

        var result = Format("""
            static string? LineEnding(string? value) => value switch
            {
            "lf" => "\n",
            "crlf" => "\r\n",
            "cr" => "\r",
            _ => null
            };
            """,
            preferences);

        result.ShouldBe("""
            static string? LineEnding(string? value) => value switch
                {
                    "lf" => "\n",
                    "crlf" => "\r\n",
                    "cr" => "\r",
                    _ => null
                };
            """);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Not_indented_overrides_general_brace_indentation()
    {
        const string source = """
            var result = value switch
            {
                true => 1,
                false => 0
            };
            """;

        Format(source,
            ("dress_switch_expression_indentation", "not_indented"),
            ("csharp_indent_braces", "true"),
            ("csharp_indent_block_contents", "true")).ShouldBe(source);
    }

    [Theory]
    [InlineData("var result = value switch { true => 1, false => 0 };")]
    [InlineData("var result = value switch\n{\n#if DEBUG\ntrue => 1,\n#endif\n_ => 0\n};")]
    [InlineData("var result = value switch\n{\ntrue =>,\n_ => 0\n};")]
    [InlineData("switch (value)\n{\ncase true:\n    break;\n}")]
    public void Preserves_unowned_or_unsafe_syntax(string source) =>
        Format(source, ("dress_switch_expression_indentation", "indented")).ShouldBe(Format(source));

    [Theory]
    [InlineData("var result = value switch\n    {\n#if DEBUG\n        true => 1,\n#endif\n        _ => 0\n    };")]
    [InlineData("var result = value switch\n    {\n        true =>,\n        _ => 0\n    };")]
    public void Configured_rule_preserves_unsafe_syntax_from_the_general_brace_rule(string source) =>
        Format(source,
            ("dress_switch_expression_indentation", "indented"),
            ("csharp_indent_braces", "false")).ShouldBe(source);

    [Fact]
    public void Unset_leaves_indentation_to_the_general_brace_rule()
    {
        const string source = "var result = value switch\n    {\n        true => 1,\n        false => 0\n    };";

        Format(source,
                ("dress_switch_expression_indentation", "unset"),
                ("csharp_indent_braces", "false"),
                ("csharp_indent_block_contents", "true"))
            .ShouldBe(Format(source,
                ("csharp_indent_braces", "false"),
                ("csharp_indent_block_contents", "true")));
    }
}
