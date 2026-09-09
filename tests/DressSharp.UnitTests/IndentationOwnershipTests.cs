using EasyAssertions;
using DressSharp.Architecture;
using DressSharp.Configuration;
using Xunit;
using static DressSharp.UnitTests.EmitterTestHarness;

namespace DressSharp.UnitTests;

public class IndentationOwnershipTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Rewritten_loop_body_retains_argument_continuation_anchors(string lineEnding)
    {
        var source = """
            void Delimited<T>(SeparatedSyntaxList<T> items, SyntaxToken close, bool spacesInside = false) where T : SyntaxNode
            {
                for (var index = 0; index < items.Count; index++)
                    AddBoundary(
                        items[index].GetFirstToken(),
                        index == 0
                            ? spacesInside ? GapStyle.DelimitedSpacedFirst : GapStyle.DelimitedFirst
                            : GapStyle.DelimitedLater);
            }
            """.ReplaceLineEndings(lineEnding);
        var expected = """
            void Delimited<T>(SeparatedSyntaxList<T> items, SyntaxToken close, bool spacesInside = false) where T : SyntaxNode
            {
                for (var index = 0; index < items.Count; index++)
                {
                    AddBoundary(
                        items[index].GetFirstToken(),
                        index == 0
                            ? spacesInside
                                ? GapStyle.DelimitedSpacedFirst
                                : GapStyle.DelimitedFirst
                            : GapStyle.DelimitedLater);
                }
            }
            """.ReplaceLineEndings(lineEnding);
        var preferences = PreferenceCatalog.Defaults
            .Select(item => (item.Key.ToName(), item.Key == RuleKey.DressNestedConditionalStyle ? "decision_ladder" : item.Default))
            .ToArray();
        var result = Format(source, preferences);
        result.ShouldBe(expected);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("\n", false)]
    [InlineData("\r\n", false)]
    [InlineData("\n", true)]
    [InlineData("\r\n", true)]
    public void Parenthesized_conditions_preserve_relative_continuation_indentation(string lineEnding, bool reindent)
    {
        var source = """
            if (BelongsToOccurrence(token, occurrence)
                && (token.IsKind(SyntaxKind.QuestionToken)
                && token.Parent is ConditionalAccessExpressionSyntax
                || (token.IsKind(SyntaxKind.DotToken)
                    && !token.GetPreviousToken().IsKind(SyntaxKind.QuestionToken))
                || token.IsKind(SyntaxKind.MinusGreaterThanToken)))
            {
                AddBoundary(index, GapStyle.CompactItem);
            }
            """.ReplaceLineEndings(lineEnding);
        var expected = source;
        if (reindent)
        {
            var header = "void M()" + lineEnding + "{" + lineEnding;
            var footer = lineEnding + "}";
            expected = header + string.Join(lineEnding, source.Split(lineEnding).Select(line => "    " + line)) + footer;
            source = header + source + footer;
        }
        var preferences = new[]
        {
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_braces", "false"),
            ("dress_binary_expressions_layout", "auto"),
            ("dotnet_style_operator_placement_when_wrapping", "beginning_of_line"),
            ("max_line_length", "160")
        };
        var result = Format(source, preferences);
        result.ShouldBe(expected);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("\n", "unset", "100")]
    [InlineData("\r\n", "unset", "100")]
    [InlineData("\n", "auto", "100")]
    [InlineData("\r\n", "auto", "100")]
    [InlineData("\n", "auto", "160")]
    [InlineData("\r\n", "auto", "160")]
    public void Conditional_branches_follow_their_multiline_argument(string lineEnding, string argumentsLayout, string maximumLineLength)
    {
        var source = """
            AddBoundary(
                right,
                index == 0 || !right.GetPreviousToken().IsKind(SyntaxKind.CommaToken)
                    ? GapStyle.SeparatedFirst
                    : GapStyle.SeparatedLater);
            """.ReplaceLineEndings(lineEnding);
        var preferences = new[]
        {
            ("csharp_indent_block_contents", "true"),
            ("dress_arguments_layout", argumentsLayout),
            ("dress_conditional_expressions_layout", "auto"),
            ("dress_nested_conditional_style", "decision_ladder"),
            ("max_line_length", maximumLineLength)
        };
        var result = Format(source, preferences);
        result.ShouldBe(source);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Binary_continuations_follow_their_multiline_argument()
    {
        const string source = """
            M(
                first,
                second
                    && third);
            """;
        var preferences = new[]
        {
            ("csharp_indent_block_contents", "true"),
            ("dress_binary_expressions_layout", "always_multi")
        };
        var result = Format(source, preferences);
        result.ShouldBe(source);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Nested_block_braces_and_wrapping_share_the_same_anchor()
    {
        const string source = """
            if (ready)
                {
                        {
                            result = firstCondition
                                && secondCondition;
                        }
                }
            """;
        var preferences = new[]
        {
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_braces", "true"),
            ("dress_binary_expressions_layout", "always_multi")
        };
        var result = Format(source.Replace("    ", ""), preferences);
        result.ShouldBe(source);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Disabling_block_indentation_also_changes_wrapping_anchors()
    {
        const string source = """
            class C
            {
                void M()
                {
                if (firstCondition
                    && secondCondition)
                {
                Work();
                }
                }
            }
            """;
        var preferences = new[]
        {
            ("csharp_indent_block_contents", "false"),
            ("csharp_indent_braces", "false"),
            ("dress_binary_expressions_layout", "always_multi")
        };
        var result = Format(source.Replace("    ", ""), preferences);
        result.ShouldBe(source);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Case_preferences_apply_to_bodies_and_wrapped_expressions(bool indentLabels, bool indentContents)
    {
        var label = indentLabels ? "\t" : "";
        var statement = label + (indentContents ? "\t" : "");
        var source = $"switch (value) {{\n{label}case 0:\n{statement}if (firstCondition\n{statement}\t&& secondCondition)\n{statement}{{\n{statement}\tWork();\n{statement}}}\n}}";
        var preferences = new[]
        {
            ("indent_style", "tab"),
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_braces", "false"),
            ("csharp_indent_switch_labels", indentLabels ? "true" : "false"),
            ("csharp_indent_case_contents", indentContents ? "true" : "false"),
            ("dress_binary_expressions_layout", "always_multi")
        };
        var result = Format(source.Replace("\t", ""), preferences);
        result.ShouldBe(source);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Wrapped_headers_and_braces_follow_their_unbraced_owner(string lineEnding)
    {
        var source = """
            while (ready)
                if (firstCondition
                    && secondCondition)
                {
                    Work();
                }
                else if (thirdCondition
                    && fourthCondition)
                {
                    Other();
                }
            """.ReplaceLineEndings(lineEnding);
        var preferences = new[]
        {
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_braces", "false"),
            ("dress_binary_expressions_layout", "auto"),
            ("max_line_length", "35")
        };
        var result = Format(source, preferences);
        result.ShouldBe(source);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("if (ready)")]
    [InlineData("while (ready)")]
    [InlineData("using (resource)")]
    [InlineData("lock (gate)")]
    public void Wrapped_body_expressions_follow_their_statement_owner(string header)
    {
        var source = $$"""
            {{header}}
                result = firstCondition
                    && secondCondition;
            """;
        var preferences = new[]
        {
            ("csharp_indent_block_contents", "true"),
            ("dress_binary_expressions_layout", "always_multi")
        };
        var result = Format(source, preferences);
        result.ShouldBe(source);
        Format(result, preferences).ShouldBe(result);
    }
}
