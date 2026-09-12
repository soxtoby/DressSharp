using DressSharp.Architecture;
using DressSharp.Execution;
using DressSharp.IO;
using EasyAssertions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DressSharp.UnitTests;

public class DocumentFormatterTests
{
    [Theory]
    [InlineData("\n", false)]
    [InlineData("\n", true)]
    [InlineData("\r\n", false)]
    [InlineData("\r\n", true)]
    public async Task Reused_layout_preserves_malformed_occurrence_counts(string lineEnding, bool needsFormatting)
    {
        var preferences = new Dictionary<RuleKey, string>
            {
                [RuleKey.DressEmbeddedStatementBraces] = "balanced",
                [RuleKey.DressArgumentsLayout] = "auto",
                [RuleKey.MaxLineLength] = "40",
                [RuleKey.CSharpIndentBlockContents] = "true",
                [RuleKey.CSharpNewLineBeforeOpenBrace] = "all"
            };
        var formatter = new DocumentFormatter(new(preferences), new BenchmarkTiming());
        var expected = "class C\n{\n    void M()\n    {\n        Broken(,);\n        Run();\n    }\n}".ReplaceLineEndings(lineEnding);
        var source = needsFormatting ? expected.Replace("void M()" + lineEnding + "    {", "void M() {") : expected;

        var result = await Format(source);
        var text = SourceDocument.Decode(result.Content, result.Encoding);

        text.ShouldBe(expected);
        result.SkippedOccurrences.ShouldBe(1);
        var repeated = await Format(text);
        SourceDocument.Decode(repeated.Content, repeated.Encoding).ShouldBe(expected);
        repeated.SkippedOccurrences.ShouldBe(1);

        ValueTask<FormattedDocument> Format(string input) => formatter.Format(
            SourceDocument.FromText("test.cs", input), CSharpParseOptions.Default, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Auto_wrapping_preserves_large_initializers_with_nested_line_breaks(string lineEnding)
    {
        var items = string.Join(lineEnding, Enumerable.Range(0, 512).Select(index => $"            {index},"));
        var source = string.Join(lineEnding,
            "class C", "{", "    object Values = new[]", "        {", items,
            "            Other(", "                1,", "                2)", "        };", "}");
        (string, string)[] preferences =
        [
            ("dress_array_initializer_layout", "auto"),
            ("dress_array_initializer_indentation", "indented"),
            ("csharp_indent_block_contents", "true"),
            ("max_line_length", "80")
        ];

        var result = EmitterTestHarness.Format(source, preferences);

        result.ShouldBe(source);
        EmitterTestHarness.Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Shared_formatter_keeps_brace_and_wrapping_results_local_to_each_document(string lineEnding)
    {
        var preferences = new Dictionary<RuleKey, string>
            {
                [RuleKey.DressEmbeddedStatementBraces] = "balanced",
                [RuleKey.DressArgumentsLayout] = "auto",
                [RuleKey.MaxLineLength] = "40",
                [RuleKey.CSharpIndentBlockContents] = "true",
                [RuleKey.CSharpNewLineBeforeOpenBrace] = "all"
            };
        var formatter = new DocumentFormatter(new(preferences), new BenchmarkTiming());
        var shortSource = "class C\n{\n    void M()\n    {\n        if (ready) Run(); else Stop();\n    }\n}".ReplaceLineEndings(lineEnding);
        var longSource = shortSource.Replace("Run()", "Run(firstLongArgument, secondLongArgument, thirdLongArgument)");

        var shortResult = Format(shortSource);
        var longResult = Format(longSource);

        Conditional(shortResult).Statement.ShouldBeA<ExpressionStatementSyntax>();
        Conditional(longResult).Statement.ShouldBeA<BlockSyntax>();
        Conditional(longResult).Else!.Statement.ShouldBeA<BlockSyntax>();
        Format(shortSource).ShouldBe(shortResult);
        Format(longSource).ShouldBe(longResult);
        Format(shortResult).ShouldBe(shortResult);
        Format(longResult).ShouldBe(longResult);
        if (lineEnding == "\r\n")
            longResult.Replace("\r\n", "").ShouldNotContain("\n");
        else
            longResult.ShouldNotContain("\r");

        string Format(string source) => formatter.FormatSyntax(
            CSharpSyntaxTree.ParseText(source).GetRoot(), source, CSharpParseOptions.Default);

        static IfStatementSyntax Conditional(string source) => CSharpSyntaxTree.ParseText(source)
            .GetRoot().DescendantNodes().OfType<IfStatementSyntax>().Single();
    }
}
