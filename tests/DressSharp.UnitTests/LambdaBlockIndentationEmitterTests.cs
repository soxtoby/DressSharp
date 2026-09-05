using EasyAssertions;
using Xunit;
using static DressSharp.UnitTests.EmitterTestHarness;

namespace DressSharp.UnitTests;

public class LambdaBlockIndentationEmitterTests
{
    [Theory]
    [InlineData("x =>", "space")]
    [InlineData("async () =>", "space")]
    [InlineData("(int x,\n        int y) =>", "space")]
    [InlineData("() =>", "tab")]
    public void Uses_the_lambda_start_and_configured_indent_unit(string lambda, string style)
    {
        var source = $$"""
            var callback =
                {{lambda}}
                {
                    Work();
                };
            """;
        var expected = $$"""
            var callback =
                {{lambda}}
                    {
                        Work();
                    };
            """;
        if (style == "tab")
        {
            source = source.Replace("    ", "\t", StringComparison.Ordinal);
            expected = expected.Replace("    ", "\t", StringComparison.Ordinal);
        }
        var preferences = new[]
        {
            ("dress_lambda_block_indentation", "indented"),
            ("csharp_indent_block_contents", "true"),
            ("indent_style", style),
            ("indent_size", "4")
        };
        var result = Format(source, preferences);
        result.ShouldBe(expected);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Not_indented_overrides_general_brace_indentation()
    {
        const string source = """
            var callback = () =>
            {
                Work();
            };
            """;
        Format(source,
            ("dress_lambda_block_indentation", "not_indented"),
            ("csharp_indent_braces", "true"),
            ("csharp_indent_block_contents", "true")).ShouldBe(source);
    }

    [Theory]
    [InlineData("var callback = () => { Work(); };")]
    [InlineData("var callback = () => Work();")]
    [InlineData("var callback = delegate\n{\n    Work();\n};")]
    [InlineData("var callback = () =>\n{\n#if DEBUG\n    Work();\n#endif\n};")]
    [InlineData("var callback = () =>\n{\n    Work( ;\n};")]
    public void Preserves_unowned_or_unsafe_syntax(string source) =>
        Format(source, ("dress_lambda_block_indentation", "indented")).ShouldBe(Format(source));

    [Fact]
    public void Unset_leaves_indentation_to_existing_rules()
    {
        const string source = "var callback = () =>\n    {\n        Work();\n    };";
        Format(source,
                ("dress_lambda_block_indentation", "unset"),
                ("csharp_indent_braces", "false"),
                ("csharp_indent_block_contents", "true"))
            .ShouldBe(Format(source,
                ("csharp_indent_braces", "false"),
                ("csharp_indent_block_contents", "true")));
    }

    [Theory]
    [InlineData("indented", "    ")]
    [InlineData("not_indented", "")]
    public void Lambda_braces_follow_the_configured_offset(string preference, string offset)
    {
        const string source = """
            foreach (var config in configs)
            {
                _ = cache.GetOrAdd(
                    config,
                    key => new Lazy<bool>(() =>
                    {
                        Validate(key);
                        return true;
                    })).Value;
            }
            """;
        var expected = $$"""
            foreach (var config in configs)
            {
                _ = cache.GetOrAdd(
                    config,
                    key => new Lazy<bool>(() =>
                    {{offset}}{
                        {{offset}}Validate(key);
                        {{offset}}return true;
                    {{offset}}})).Value;
            }
            """;
        var preferences = new[]
        {
            ("dress_lambda_block_indentation", preference),
            ("csharp_indent_braces", "false"),
            ("csharp_indent_block_contents", "true")
        };
        var result = Format(source, preferences);
        result.ShouldBe(expected);
        Format(result, preferences).ShouldBe(result);
    }
}
