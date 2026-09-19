using EasyAssertions;
using Xunit;

namespace DressSharp.UnitTests;

public sealed class ClosingDelimiterPositionRuleTests
{
    public static TheoryData<string, string, string> Lists => new()
    {
        { "arguments", "Call(\n    first,\n    second", ");" },
        { "arguments", "var value = items[\n    first,\n    second", "];" },
        { "parameters", "class C(\n    int first,\n    int second", ") { }" },
        { "parameters", "class C\n{\n    int this[\n        int first,\n        int second", "] => 0;\n}" },
        { "object_initializer", "var value = new C\n{\n    First = 1,\n    Second = 2", "};" },
        { "object_initializer", "var value = new\n{\n    First = 1,\n    Second = 2", "};" },
        { "collection_initializer", "var value = new List<int>\n{\n    1,\n    2", "};" },
        { "array_initializer", "var value = new[]\n{\n    1,\n    2", "};" },
        { "with_initializer", "var value = original with\n{\n    First = 1,\n    Second = 2", "};" },
        { "collection_expression", "int[] value = [\n    1,\n    2", "];" }
    };

    [Theory]
    [MemberData(nameof(Lists))]
    public void Positions_are_independent_of_item_layout_and_reach_a_fixpoint(string kind, string body, string close)
    {
        foreach (var ending in new[] { "\n", "\r\n" })
        {
            var indent = body.StartsWith("class C\n") ? "    " : "";
            var space = close.StartsWith('}') ? " " : "";
            var ownLine = (body + "\n" + indent + close).ReplaceLineEndings(ending);
            var attached = (body + space + close).ReplaceLineEndings(ending);
            var key = $"dress_{kind}_closing_delimiter_position";
            Format(ownLine, (key, "after_last_item")).ShouldBe(attached);
            Format(attached, (key, "after_last_item")).ShouldBe(attached);
            Format(attached, (key, "own_line")).ShouldBe(ownLine);
            Format(ownLine, (key, "own_line")).ShouldBe(ownLine);
            Format(ownLine, (key, "unset")).ShouldBe(ownLine);
        }
    }

    [Theory]
    [InlineData("after_last_item", "\");")]
    [InlineData("own_line", "\"\n);")]
    public void Wrapped_throw_uses_the_requested_close(string position, string suffix)
    {
        const string message = "Materialized corpus does not match corpus/manifest.json. Run with -UpdateLock after an intentional source-manifest change.";
        var source = $"throw new InvalidOperationException(\"{message}\");";
        var preferences = new[] { ("dress_arguments_layout", "auto"), ("max_line_length", "80"),
            ("dress_arguments_closing_delimiter_position", position) };
        var result = Format(source, preferences);
        result.ShouldBe($"throw new InvalidOperationException(\n    \"{message}" + suffix);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("arguments", "Call(value);")]
    [InlineData("parameters", "class C(int value) { }")]
    [InlineData("object_initializer", "var value = new C { Value = 1 };")]
    [InlineData("collection_initializer", "var value = new List<int> { 1 };")]
    [InlineData("array_initializer", "var value = new[] { 1 };")]
    [InlineData("with_initializer", "var value = original with { Value = 1 };")]
    [InlineData("collection_expression", "int[] value = [1];")]
    public void Single_line_lists_are_unchanged(string kind, string source)
    {
        foreach (var position in new[] { "own_line", "after_last_item" })
            Format(source, ($"dress_{kind}_closing_delimiter_position", position)).ShouldBe(source);
    }

    [Fact]
    public void Line_comment_cannot_swallow_the_close()
    {
        const string source = "Call(\n    value // keep\n);";
        var result = Format(source, ("dress_arguments_closing_delimiter_position", "after_last_item"));
        result.ShouldBe(source);
        Format(result, ("dress_arguments_closing_delimiter_position", "after_last_item")).ShouldBe(result);
    }

    [Fact]
    public void Nested_lists_apply_their_own_preferences()
    {
        const string source = "Call(\n    new[]\n    {\n        1,\n        2\n    }\n);";
        var preferences = new[] { ("dress_arguments_closing_delimiter_position", "after_last_item"),
            ("dress_array_initializer_closing_delimiter_position", "own_line") };
        var result = Format(source, preferences);
        result.ShouldBe("Call(\n    new[]\n    {\n        1,\n        2\n    });");
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Expanded_initializer_allows_an_attached_close()
    {
        var preferences = new[] { ("dress_array_initializer_layout", "expanded"),
            ("dress_array_initializer_closing_delimiter_position", "after_last_item") };
        var result = Format("var value = new[] { 1, 2 };", preferences);
        result.ShouldBe("var value = new[]\n{\n    1,\n    2 };");
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("unset")]
    [InlineData("auto")]
    public void A_close_only_line_can_be_attached(string layout)
    {
        Format("Call(value\n);", ("dress_arguments_layout", layout),
            ("dress_arguments_closing_delimiter_position", "after_last_item")).ShouldBe("Call(value);");
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void A_multiline_string_publishes_its_close_before_the_enclosing_list_wraps(string ending)
    {
        var preferences = new[] { ("dress_arguments_layout", "auto"),
            ("dress_arguments_closing_delimiter_position", "own_line"), ("max_line_length", "160") };
        var source = "Assert.Equal(2, Execute(@\"first\nsecond\"));\n".ReplaceLineEndings(ending);
        var result = Format(source, preferences);
        result.ShouldBe("Assert.Equal(\n    2,\n    Execute(@\"first\nsecond\"\n    )\n);\n".ReplaceLineEndings(ending));
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Closing_a_multiline_lambda_does_not_expand_its_items_on_the_next_run()
    {
        const string source = "Call(() =>\n{\n    Run();\n});";
        var preferences = new[] { ("dress_arguments_layout", "auto"),
            ("dress_arguments_closing_delimiter_position", "own_line"), ("max_line_length", "160") };
        var result = Format(source, preferences);
        result.ShouldBe("Call(() =>\n{\n    Run();\n}\n);");
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("after_last_item")]
    [InlineData("own_line")]
    public void Malformed_lists_and_comment_boundaries_are_preserved(string position)
    {
        foreach (var source in new[] { "Call(\n    ,);", "Call(\n    value /* keep */);", "Call(\n#if FLAG\n    value\n#endif\n);" })
            Format(source, ("dress_arguments_closing_delimiter_position", position)).ShouldBe(source);
    }

    [Fact]
    public void Trailing_comma_stays_before_the_attached_initializer_close()
    {
        var preferences = new[] { ("dress_array_initializer_closing_delimiter_position", "after_last_item") };
        var result = Format("var value = new[]\n{\n    1,\n};", preferences);
        result.ShouldBe("var value = new[]\n{\n    1, };");
        Format(result, preferences).ShouldBe(result);
    }

    static string Format(string source, params (string Key, string Value)[] preferences) =>
        EmitterTestHarness.Format(source, preferences);

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Consecutive_closes_with_matching_opening_indents_share_a_line(string ending)
    {
        const string source = "var corpusHash = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(\n    string.Join('\\n', entries.Select(entry => $\"{entry.path}:{entry.sha256}\")))));";
        const string expected = "var corpusHash = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(\n    string.Join('\\n', entries.Select(entry => $\"{entry.path}:{entry.sha256}\"))\n)));";
        var preferences = new[] { ("dress_arguments_closing_delimiter_position", "own_line") };
        var result = Format(source.ReplaceLineEndings(ending), preferences);
        result.ShouldBe(expected.ReplaceLineEndings(ending));
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Different_opening_indents_keep_separate_closing_lines()
    {
        const string source = "Outer(\n    Middle(Inner(\n        value)));";
        const string expected = "Outer(\n    Middle(Inner(\n        value\n    ))\n);";
        var preferences = new[] { ("dress_arguments_closing_delimiter_position", "own_line") };
        var result = Format(source, preferences);
        result.ShouldBe(expected);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Different_delimiter_types_can_share_a_closing_line()
    {
        const string source = "Call(new[] {\n    1,\n    2 });";
        const string expected = "Call(new[] {\n    1,\n    2\n});";
        var preferences = new[] { ("dress_arguments_closing_delimiter_position", "own_line"),
            ("dress_array_initializer_closing_delimiter_position", "own_line") };
        var result = Format(source, preferences);
        result.ShouldBe(expected);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Comments_between_closes_prevent_grouping()
    {
        const string source = "Outer(Inner(\n    value\n) // keep\n);";
        var preferences = new[] { ("dress_arguments_closing_delimiter_position", "own_line") };
        Format(source, preferences).ShouldBe(source);
    }
}
