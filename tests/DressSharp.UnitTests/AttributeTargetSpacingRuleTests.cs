using EasyAssertions;
using Xunit;
using static DressSharp.UnitTests.EmitterTestHarness;

namespace DressSharp.UnitTests;

public class AttributeTargetSpacingRuleTests
{
    const string Key = "dress_space_after_attribute_target_colon";

    [Fact]
    public void Auto_layout_measures_the_selected_target_spacing()
    {
        Format("[assembly:A]", (Key, "false"), ("dress_attributes_layout", "auto"), ("max_line_length", "12"))
            .ShouldBe("[assembly:A]");
        Format("[assembly:A]", (Key, "true"), ("dress_attributes_layout", "auto"), ("max_line_length", "12"))
            .ShouldBe("[assembly:\n    A\n]");
    }

    [Theory]
    [InlineData("[assembly:  A]", "[assembly: A]", "[assembly:A]")]
    [InlineData("[module:  A]", "[module: A]", "[module:A]")]
    [InlineData("class C { [return:  A] int M() => 1; }", "class C { [return: A] int M() => 1; }", "class C { [return:A] int M() => 1; }")]
    [InlineData("class C { [field:  A] int x; }", "class C { [field: A] int x; }", "class C { [field:A] int x; }")]
    public void Target_spacing_respects_preference_with_and_without_layout(string source, string spaced, string compact)
    {
        foreach (var layout in new[] { "unset", "always_single", "auto" })
        {
            foreach (var value in new[] { "true", "false", "unset" })
            {
                var expected = value switch { "true" => spaced, "false" => compact, _ => source };
                var result = Format(source, (Key, value), ("dress_attributes_layout", layout));
                result.ShouldBe(expected);
                Format(result, (Key, value), ("dress_attributes_layout", layout)).ShouldBe(result);
            }
        }
        Format(source).ShouldBe(source);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Multiline_layout_and_spacing_are_idempotent(string newline)
    {
        foreach (var value in new[] { "true", "false", "unset" })
        {
            var preferences = new[] { (Key, value), ("dress_attributes_layout", "always_multi"), ("end_of_line", newline == "\n" ? "lf" : "crlf") };
            var result = Format($"[assembly: A, B]{newline}", preferences);
            result.ShouldBe($"[assembly:{newline}    A,{newline}    B{newline}]{newline}");
            Format(result, preferences).ShouldBe(result);
        }
    }

    [Theory]
    [InlineData("[assembly:/* keep */ A]")]
    [InlineData("[assembly:\nA]")]
    [InlineData("[assembly:\r\nA]")]
    [InlineData("class C : B { int M() => true ? 1 : 2; void N() { M(value: 1); } }")]
    public void Spacing_preserves_comments_newlines_and_other_colons(string source)
    {
        Format(source, (Key, "true")).ShouldBe(source);
        Format(source, (Key, "false")).ShouldBe(source);
    }

    [Theory]
    [InlineData("always_single")]
    [InlineData("auto")]
    public void Attribute_layout_preserves_target_spacing(string layout)
    {
        const string source = "[assembly: TaskDescription(\"Compare DressSharp with pinned dotnet format using the versioned benchmark protocol.\")]";
        Format(source, ("dress_attributes_layout", layout)).ShouldBe(source);
    }
}
