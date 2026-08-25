using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DressSharp.UnitTests;

public class EmbeddedStatementRuleTests
{
    [Theory]
    [InlineData("same_line", " ")]
    [InlineData("next_line", "\n            ")]
    public void Placement_collapses_existing_body_whitespace(string placement, string expectedGap)
    {
        const string source = "class C\n{\n    void M()\n    {\n        if (ready)       \n\n\n            Run();\n    }\n}";

        var result = Lines(Format(
            source,
            ("dress_embedded_statement_placement", placement),
            ("csharp_indent_block_contents", "true")));
        var conditional = Root(result).DescendantNodes().OfType<IfStatementSyntax>().Single();

        Assert.Equal(expectedGap, Gap(result, conditional.CloseParenToken, conditional.Statement.GetFirstToken()));
    }

    [Theory]
    [InlineData("same_line", " ")]
    [InlineData("next_line", "\n        ")]
    public void Placement_owns_braced_bodies(string placement, string expectedGap)
    {
        const string source = "class C\n{\n    void M()\n    {\n        if (ready)       { Run(); }\n    }\n}";

        var result = Format(
            source,
            ("dress_embedded_statement_placement", placement),
            ("csharp_new_line_before_open_brace", placement == "same_line" ? "all" : "none"),
            ("csharp_indent_block_contents", "true"));
        result = Lines(result);
        var conditional = Root(result).DescendantNodes().OfType<IfStatementSyntax>().Single();

        Assert.Equal(expectedGap, Gap(result, conditional.CloseParenToken, conditional.Statement.GetFirstToken()));
    }

    [Theory]
    [InlineData("if (ready) Run();")]
    [InlineData("if (ready) Run(); else Stop();")]
    [InlineData("while (ready) Run();")]
    [InlineData("do Run(); while (ready);")]
    [InlineData("for (;;) Run();")]
    [InlineData("foreach (var item in items) Run();")]
    [InlineData("foreach (var (key, value) in items) Run();")]
    [InlineData("await foreach (var item in items) Run();")]
    [InlineData("using (resource) Run();")]
    [InlineData("await using (resource) Run();")]
    [InlineData("lock (gate) Run();")]
    [InlineData("fixed (int* pointer = values) Run();")]
    public void Placement_and_braces_support_every_brace_optional_embedded_statement(string statement)
    {
        var source = $"class C {{ async System.Threading.Tasks.Task M() {{ {statement} }} }}";
        var result = Format(source, ("dress_embedded_statement_placement", "next_line"));
        var run = Root(result).DescendantNodes().OfType<ExpressionStatementSyntax>()
            .Single(candidate => candidate.ToString() == "Run();");

        var first = run.GetFirstToken();
        Assert.Equal("\n    ", Gap(Lines(result), first.GetPreviousToken(), first));

        var bracedRun = Root(Format(source, ("dress_embedded_statement_braces", "always")))
            .DescendantNodes().OfType<ExpressionStatementSyntax>()
            .Single(candidate => candidate.ToString() == "Run();");
        Assert.IsType<BlockSyntax>(bracedRun.Parent);
    }

    [Fact]
    public void Else_if_remains_a_chain_continuation()
    {
        const string source = "class C { void M() { if (a) A(); else if (b) B(); else C(); } }";
        var result = Format(source, ("dress_embedded_statement_placement", "next_line"));
        var root = Root(result);
        var outer = root.DescendantNodes().OfType<IfStatementSyntax>().First();
        var nested = Assert.IsType<IfStatementSyntax>(outer.Else!.Statement);

        Assert.Equal(
            outer.Else.ElseKeyword.GetLocation().GetLineSpan().StartLinePosition.Line,
            nested.IfKeyword.GetLocation().GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void Compact_braces_a_body_made_multiline_by_wrapping()
    {
        const string source = "class C { void M() { if (ready) Run(alpha, beta); } }";
        var result = Format(
            source,
            ("dress_embedded_statement_braces", "compact"),
            ("dress_arguments_layout", "always_multi"));

        Assert.IsType<BlockSyntax>(Root(result).DescendantNodes().OfType<IfStatementSyntax>().Single().Statement);
    }

    [Fact]
    public void Balanced_propagates_a_multiline_body_across_the_chain()
    {
        const string source = "class C { void M() { if (a) A(); else if (b) B(alpha, beta); else C(); } }";
        var result = Format(
            source,
            ("dress_embedded_statement_braces", "balanced"),
            ("dress_arguments_layout", "always_multi"));
        var chain = Root(result).DescendantNodes().OfType<IfStatementSyntax>().ToArray();

        Assert.All(chain, conditional => Assert.IsType<BlockSyntax>(conditional.Statement));
        Assert.IsType<BlockSyntax>(chain[0].Else!.Statement is IfStatementSyntax nested ? nested.Else!.Statement : null);
    }

    [Fact]
    public void Balanced_propagates_a_multiline_header_across_the_chain()
    {
        const string source = "class C { void M() { if (first && second) A(); else B(); } }";
        var conditional = Root(Format(
                source,
                ("dress_embedded_statement_braces", "balanced"),
                ("dress_braces_for_multiline_statement_header", "true"),
                ("dress_binary_expressions_layout", "always_multi")))
            .DescendantNodes().OfType<IfStatementSyntax>().Single();

        Assert.IsType<BlockSyntax>(conditional.Statement);
        Assert.IsType<BlockSyntax>(conditional.Else!.Statement);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Multiline_header_can_contribute_a_brace_constraint(string preference, bool expectedBlock)
    {
        const string source = "class C { void M() { if (first && second) Run(); } }";
        var result = Format(
            source,
            ("dress_embedded_statement_braces", "compact"),
            ("dress_braces_for_multiline_statement_header", preference),
            ("dress_binary_expressions_layout", "always_multi"));
        var body = Root(result).DescendantNodes().OfType<IfStatementSyntax>().Single().Statement;

        Assert.Equal(expectedBlock, body is BlockSyntax);
    }

    [Fact]
    public void Header_preference_does_not_remove_unconstrained_existing_braces()
    {
        const string source = "class C { void M() { if (ready) { Run(); } } }";
        var body = Root(Format(source, ("dress_braces_for_multiline_statement_header", "true")))
            .DescendantNodes().OfType<IfStatementSyntax>().Single().Statement;

        Assert.IsType<BlockSyntax>(body);
    }

    [Fact]
    public void Do_trailing_while_is_part_of_the_statement_header()
    {
        const string source = "class C { void M() { do Run(); while (first && second); } }";
        var body = Root(Format(
                source,
                ("dress_embedded_statement_braces", "compact"),
                ("dress_braces_for_multiline_statement_header", "true"),
                ("dress_binary_expressions_layout", "always_multi")))
            .DescendantNodes().OfType<DoStatementSyntax>().Single().Statement;

        Assert.IsType<BlockSyntax>(body);
    }

    [Fact]
    public void Nested_body_lines_constrain_the_owning_body()
    {
        const string source = "class C { void M() { while (ready) if (other) Run(); } }";
        var loop = Root(Format(
                source,
                ("dress_embedded_statement_placement", "next_line"),
                ("dress_embedded_statement_braces", "compact")))
            .DescendantNodes().OfType<WhileStatementSyntax>().Single();

        Assert.IsType<BlockSyntax>(loop.Statement);
    }

    [Fact]
    public void Next_line_uses_the_source_line_ending()
    {
        const string source = "class C\r\n{\r\n    void M()\r\n    {\r\n        if (ready) Run();\r\n    }\r\n}";
        var result = Format(
            source,
            ("dress_embedded_statement_placement", "next_line"),
            ("csharp_indent_block_contents", "true"));
        var conditional = Root(result).DescendantNodes().OfType<IfStatementSyntax>().Single();

        Assert.Equal("\r\n            ", Gap(result, conditional.CloseParenToken, conditional.Statement.GetFirstToken()));
    }

    [Theory]
    [InlineData("compact", false)]
    [InlineData("balanced", false)]
    [InlineData("always", true)]
    public void Empty_bodies_use_the_mode_canonical_form(string mode, bool expectedBlock)
    {
        const string source = "class C { void M() { if (ready) { } } }";
        var body = Root(Format(source, ("dress_embedded_statement_braces", mode)))
            .DescendantNodes().OfType<IfStatementSyntax>().Single().Statement;

        Assert.Equal(expectedBlock, body is BlockSyntax);
        Assert.Equal(!expectedBlock, body is EmptyStatementSyntax);
    }

    [Fact]
    public void Meaningful_trivia_preserves_the_owned_boundary()
    {
        const string source = "class C { void M() { if (ready) /* keep */ Run(); } }";

        Assert.Equal(source, Format(source, ("dress_embedded_statement_placement", "next_line")));
    }

    [Fact]
    public void Significant_trivia_prevents_brace_removal()
    {
        const string source = "class C { void M() { if (/* keep */ ready) { Run(); } } }";

        Assert.Equal(source, Format(source, ("dress_embedded_statement_braces", "compact")));
    }

    [Fact]
    public void Malformed_occurrences_are_unchanged()
    {
        const string source = "class C { void M() { if (ready       Run(); } }";

        Assert.Equal(source, Format(source, ("dress_embedded_statement_placement", "next_line")));
    }

    [Fact]
    public void Unset_preferences_make_no_change()
    {
        const string source = "class C { void M() { if (ready)       { Run(); } } }";

        Assert.Equal(source, Format(
            source,
            ("dress_embedded_statement_placement", "unset"),
            ("dress_embedded_statement_braces", "unset"),
            ("dress_braces_for_multiline_statement_header", "unset")));
    }

    [Fact]
    public void Familiar_contract_formats_the_regression_and_is_idempotent()
    {
        const string source = "class C\n{\n    bool Equal(C right)\n    {\n        if (!right.Try(key, out var other) || !value.Equals(other))             return false;\n        return true;\n    }\n}";
        var preferences = new[]
        {
            ("dress_embedded_statement_placement", "next_line"),
            ("dress_embedded_statement_braces", "balanced"),
            ("dress_braces_for_multiline_statement_header", "true"),
            ("dress_binary_expressions_layout", "always_multi"),
            ("csharp_new_line_before_open_brace", "all"),
            ("csharp_preserve_single_line_blocks", "false"),
            ("csharp_preserve_single_line_statements", "false"),
            ("csharp_indent_block_contents", "true")
        };

        var first = Format(source, preferences);
        var normalized = Lines(first);

        Assert.Contains("if (!right.Try(key, out var other)\n            || !value.Equals(other))\n        {\n            return false;\n        }", normalized);
        Assert.Equal(first, Format(first, preferences));
    }

    static string Format(string source, params (string Key, string Value)[] preferences) =>
        EmitterTestHarness.Format(source, preferences);

    static CompilationUnitSyntax Root(string source) =>
        CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();

    static string Gap(string source, Microsoft.CodeAnalysis.SyntaxToken left, Microsoft.CodeAnalysis.SyntaxToken right) =>
        source[left.Span.End..right.SpanStart];

    static string Lines(string source) => source.Replace("\r\n", "\n");
}
