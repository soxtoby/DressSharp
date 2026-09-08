using Xunit;

namespace DressSharp.UnitTests;

public class NestedConditionalStyleTests
{
    [Theory]
    [InlineData("flat", "beginning_of_line", "first\n    ? one\n    : second\n    ? two\n    : three")]
    [InlineData("flat", "end_of_line", "first ?\n    one :\n    second ?\n    two :\n    three")]
    [InlineData("staircase", "beginning_of_line", "first\n    ? one\n    : second\n        ? two\n        : three")]
    [InlineData("staircase", "end_of_line", "first ?\n    one :\n    second ?\n        two :\n        three")]
    [InlineData("decision_ladder", "beginning_of_line", "\n    first ? one\n    : second ? two\n    : three")]
    [InlineData("decision_ladder", "end_of_line", "\n    first ? one :\n    second ? two :\n    three")]
    public void Styles_and_operator_positions(string style, string placement, string expression)
    {
        var expected = "var result =" + (expression.StartsWith('\n') ? "" : " ") + expression + ";";
        Check("var result = first ? one : second ? two : three;", expected,
            ("dress_conditional_expressions_layout", "always_multi"),
            ("dress_nested_conditional_style", style),
            ("dotnet_style_operator_placement_when_wrapping", placement));
    }

    [Theory]
    [InlineData("staircase")]
    [InlineData("decision_ladder")]
    public void True_branch_nesting_remains_a_tree(string style) => Check(
        "var result = outer ? inner ? one : two : three;",
        "var result = outer\n    ? inner\n        ? one\n        : two\n    : three;",
        ("dress_conditional_expressions_layout", "always_multi"), ("dress_nested_conditional_style", style));

    [Theory]
    [InlineData("beginning_of_line", "var result = first\n    + second + third;")]
    [InlineData("end_of_line", "var result = first +\n    second + third;")]
    public void Operator_position_alone_preserves_which_operators_wrap(string placement, string expected) => Check(
        "var result = first\n    + second + third;", expected,
        ("dotnet_style_operator_placement_when_wrapping", placement));

    [Theory]
    [InlineData("beginning_of_line", "var result = first\n    + second\n    + third;")]
    [InlineData("end_of_line", "var result = first +\n    second +\n    third;")]
    public void Binary_wrapping_obeys_position(string placement, string expected) => Check(
        "var result = first + second + third;", expected,
        ("dress_binary_expressions_layout", "always_multi"),
        ("dotnet_style_operator_placement_when_wrapping", placement));

    [Fact]
    public void Style_alone_leaves_single_line_expressions_alone() => Check(
        "var result = first ? one : second ? two : three;",
        "var result = first ? one : second ? two : three;",
        ("dress_nested_conditional_style", "decision_ladder"));

    [Fact]
    public void Style_alone_reshapes_existing_wrapping() => Check(
        "var result = first\n    ? one\n    : second\n    ? two\n    : three;",
        "var result = first\n    ? one\n    : second\n        ? two\n        : three;",
        ("dress_nested_conditional_style", "staircase"));

    [Fact]
    public void Ladder_falls_back_for_multiline_arguments() => Check(
        "var result = first ? Call(\n    one,\n    two\n) : second ? two : three;",
        "var result = first\n    ? Call(\n    one,\n    two\n)\n    : second\n        ? two\n        : three;",
        ("dress_conditional_expressions_layout", "always_multi"),
        ("dress_nested_conditional_style", "decision_ladder"));

    [Fact]
    public void Commented_operator_boundaries_are_preserved() => Check(
        "var result = first // reason\n    ? one : second ? two : three;",
        "var result = first // reason\n    ? one : second ? two : three;",
        ("dress_conditional_expressions_layout", "always_multi"),
        ("dress_nested_conditional_style", "decision_ladder"),
        ("dotnet_style_operator_placement_when_wrapping", "end_of_line"));

    [Fact]
    public void Ladder_falls_back_when_another_rule_wraps_a_branch() => Check(
        "var result = first ? Call(one, two) : second ? two : three;",
        "var result = first\n    ? Call(\n        one,\n        two\n    )\n    : second\n        ? two\n        : three;",
        ("dress_conditional_expressions_layout", "always_multi"),
        ("dress_arguments_layout", "always_multi"),
        ("dress_nested_conditional_style", "decision_ladder"));

    [Fact]
    public void Auto_wraps_into_a_ladder() => Check(
        "var result = first ? one : second ? two : three;",
        "var result =\n    first ? one :\n    second ? two :\n    three;",
        ("dress_conditional_expressions_layout", "auto"),
        ("max_line_length", "30"),
        ("dress_nested_conditional_style", "decision_ladder"),
        ("dotnet_style_operator_placement_when_wrapping", "end_of_line"));

    [Fact]
    public void Single_line_layout_overrides_nested_style() => Check(
        "var result = first\n    ? one\n    : second\n        ? two\n        : three;",
        "var result = first ? one : second ? two : three;",
        ("dress_conditional_expressions_layout", "always_single"),
        ("dress_nested_conditional_style", "decision_ladder"));

    [Fact]
    public void Staircase_uses_configured_tabs() => Check(
        "var result = first ? one : second ? two : three;",
        "var result = first\n\t? one\n\t: second\n\t\t? two\n\t\t: three;",
        ("dress_conditional_expressions_layout", "always_multi"),
        ("dress_nested_conditional_style", "staircase"), ("indent_style", "tab"));

    [Fact]
    public void Ladder_uses_structural_indentation() => Check(
        "class C\n{\n    int M()\n    {\n        return first ? one : second ? two : three;\n    }\n}",
        "class C\n{\n    int M()\n    {\n        return\n            first ? one\n            : second ? two\n            : three;\n    }\n}",
        ("dress_conditional_expressions_layout", "always_multi"),
        ("dress_nested_conditional_style", "decision_ladder"),
        ("csharp_indent_block_contents", "true"));

    [Fact]
    public void Ladder_uses_a_branch_compacted_by_another_rule() => Check(
        "var result = first ? Call(\n    one,\n    two\n) : second ? two : three;",
        "var result =\n    first ? Call(one, two)\n    : second ? two\n    : three;",
        ("dress_conditional_expressions_layout", "always_multi"),
        ("dress_arguments_layout", "always_single"),
        ("dress_nested_conditional_style", "decision_ladder"));

    [Fact]
    public void Unset_styles_leave_existing_layout_behavior() => Check(
        "var result = first ? one : second ? two : three;",
        "var result = first\n    ? one\n    : second\n    ? two\n    : three;",
        ("dress_conditional_expressions_layout", "always_multi"),
        ("dress_nested_conditional_style", "unset"),
        ("dotnet_style_operator_placement_when_wrapping", "unset"));

    static void Check(string source, string expected, params (string Key, string Value)[] preferences)
    {
        foreach (var lineEnding in new[] { "\n", "\r\n" })
        {
            var input = source.Replace("\n", lineEnding) + lineEnding;
            var output = EmitterTestHarness.Format(input, preferences);
            Assert.Equal(expected.Replace("\n", lineEnding) + lineEnding, output);
            Assert.Equal(output, EmitterTestHarness.Format(output, preferences));
        }
    }
}
