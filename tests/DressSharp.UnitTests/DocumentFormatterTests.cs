using DressSharp.Architecture;
using DressSharp.Configuration;
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
            SourceDocument.FromText("test.cs", input),
            CSharpParseOptions.Default,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Each_context_formats_the_region_it_enables()
    {
        var formatter = new DocumentFormatter(new(new Dictionary<RuleKey, string> { [RuleKey.CSharpSpaceAfterComma] = "true" }), new BenchmarkTiming());
        const string source = "#if A\nclass A { void M(int a,int b) { } }\n#else\nclass B { void M(int a,int b) { } }\n#endif\n";
        const string expected = "#if A\nclass A { void M(int a, int b) { } }\n#else\nclass B { void M(int a, int b) { } }\n#endif\n";
        var contexts = new[] { CSharpParseOptions.Default.WithPreprocessorSymbols("A"), CSharpParseOptions.Default };

        var result = await formatter.Format(SourceDocument.FromText("test.cs", source), contexts, TestContext.Current.CancellationToken);

        result.ContextsDisagree.ShouldBe(false);
        SourceDocument.Decode(result.Content, result.Encoding).ShouldBe(expected);
        var repeated = await formatter.Format(SourceDocument.FromText("test.cs", expected), contexts, TestContext.Current.CancellationToken);
        SourceDocument.Decode(repeated.Content, repeated.Encoding).ShouldBe(expected);
    }

    [Fact]
    public async Task Contexts_settle_a_member_that_spans_a_directive()
    {
        var preferences = new Dictionary<RuleKey, string>
            {
                [RuleKey.IndentStyle] = "space",
                [RuleKey.IndentSize] = "4",
                [RuleKey.CSharpIndentBlockContents] = "true",
                [RuleKey.CSharpNewLineBeforeOpenBrace] = "all",
            };
        var formatter = new DocumentFormatter(new(preferences), new BenchmarkTiming());
        // Each context sees a different method header over the same body, and moves only its own brace.
        const string source = "class C\n{\n#if A\n    void M() {\n#else\n    void N() {\n#endif\n    int x = 1; }\n}\n";
        const string expected = "class C\n{\n#if A\n    void M()\n    {\n#else\n    void N()\n    {\n#endif\n    int x = 1; }\n}\n";
        var contexts = new[] { CSharpParseOptions.Default.WithPreprocessorSymbols("A"), CSharpParseOptions.Default };

        var result = await formatter.Format(SourceDocument.FromText("test.cs", source), contexts, TestContext.Current.CancellationToken);

        result.ContextsDisagree.ShouldBe(false);
        SourceDocument.Decode(result.Content, result.Encoding).ShouldBe(expected);
    }

    [Theory]
    [InlineData("class C { void M() { var x = new Options { Callback = () => { if (a) Work(); }, Name = \"n\" }; } }")]
    [InlineData("class C { object M(int[] xs) { var q = from x in xs where x > 0 select x; return q; } }")]
    public void Default_preferences_settle_a_layout_another_rule_moved(string source)
    {
        // Placing the body on its own line makes the lambda span lines, which is what the auto
        // initializer around it measures; the written text has to reflect that on the first run.
        var preferences = PreferenceCatalog.Defaults
            .Select(preference => (preference.Key.ToName(), preference.Default))
            .ToArray();
        var first = EmitterTestHarness.Format(source, preferences);
        EmitterTestHarness.Format(first, preferences).ShouldBe(first);
    }

    [Fact]
    public void A_nested_list_is_indented_from_where_its_item_stands_without_a_maximum()
    {
        // No maximum means no width is measured, but the item a list puts on its own line still
        // has to be known to the list nested inside it, or that list is indented from the wrong
        // place and moves again on the next run.
        var preferences = PreferenceCatalog.Defaults
            .Where(preference => preference.Key is not (
                RuleKey.MaxLineLength
                or RuleKey.DressArgumentsLayout
                or RuleKey.DressArgumentsClosingDelimiterPosition
                or RuleKey.DressCollectionExpressionClosingDelimiterPosition
                or RuleKey.DressMethodBody))
            .Select(preference => (preference.Key.ToName(), preference.Default))
            .Append(("max_line_length", "unset"))
            .Append(("dress_arguments_layout", "always_multi"))
            .Append(("dress_arguments_closing_delimiter_position", "own_line"))
            .Append(("dress_collection_expression_closing_delimiter_position", "own_line"))
            .Append(("dress_method_body", "expression"))
            .ToArray();
        var first = EmitterTestHarness.Format("""
            class C
            {
                void M()
                {
                    Call([First(a, b), c], d);
                }
            }
            """, preferences);
        first.ShouldBe("""
            class C
            {
                void M() => Call(
                    [First(
                            a,
                            b
                    ), c
                    ],
                    d
                );
            }
            """);
        EmitterTestHarness.Format(first, preferences).ShouldBe(first);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Auto_wrapping_preserves_large_initializers_with_nested_line_breaks(string lineEnding)
    {
        var items = string.Join(lineEnding, Enumerable.Range(0, 512).Select(index => $"            {index},"));
        var source = string.Join(
            lineEnding,
            "class C",
            "{",
            "    object Values = new[]",
            "        {",
            items,
            "            Other(",
            "                1,",
            "                2)",
            "        };",
            "}");
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
            CSharpSyntaxTree.ParseText(source).GetRoot(),
            source,
            CSharpParseOptions.Default);

        static IfStatementSyntax Conditional(string source) => CSharpSyntaxTree.ParseText(source)
            .GetRoot().DescendantNodes().OfType<IfStatementSyntax>().Single();
    }
}
