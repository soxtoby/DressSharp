using DressSharp.Architecture;
using DressSharp.Configuration;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using EasyAssertions;
using static DressSharp.UnitTests.EmitterTestHarness;

namespace DressSharp.UnitTests;

public class EmbeddedStatementRuleTests
{
    [Theory]
    [InlineData("same_line", " ")]
    [InlineData("next_line", "\n            ")]
    public void Placement_collapses_existing_body_whitespace(string placement, string expectedGap)
    {
        var result = Format("""
                class C
                {
                    void M()
                    {
                        if (ready)


                            Run();
                    }
                }
                """.ReplaceLineEndings("\n"),
                ("dress_embedded_statement_placement", placement),
                ("csharp_indent_block_contents", "true"));
        var conditional = Root(result)
            .DescendantNodes().OfType<IfStatementSyntax>().Single();

        Gap(result,
            conditional.CloseParenToken,
            conditional.Statement.GetFirstToken()).ShouldBe(expectedGap);
    }

    [Theory]
    [InlineData("same_line", " ")]
    [InlineData("next_line", "\n        ")]
    public void Placement_owns_braced_bodies(string placement, string expectedGap)
    {
        var result = Format("""
            class C
            {
                void M()
                {
                    if (ready)       { Run(); }
                }
            }
            """.ReplaceLineEndings("\n"),
            ("dress_embedded_statement_placement", placement),
            ("csharp_new_line_before_open_brace", placement == "same_line" ? "all" : "none"),
            ("csharp_indent_block_contents", "true"));
        var conditional = Root(result).DescendantNodes().OfType<IfStatementSyntax>().Single();

        Gap(result, conditional.CloseParenToken, conditional.Statement.GetFirstToken()).ShouldBe(expectedGap);
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
        Gap(result, first.GetPreviousToken(), first).ShouldBe("\n    ");

        var bracedRun = Root(Format(source, ("dress_embedded_statement_braces", "always")))
            .DescendantNodes().OfType<ExpressionStatementSyntax>()
            .Single(candidate => candidate.ToString() == "Run();");
        bracedRun.Parent.ShouldBeA<BlockSyntax>();
    }

    [Fact]
    public void Else_if_remains_a_chain_continuation()
    {
        var result = Format(
            "class C { void M() { if (a) A(); else if (b) B(); else C(); } }",
            ("dress_embedded_statement_placement", "next_line"));
        var root = Root(result);
        var outer = root.DescendantNodes().OfType<IfStatementSyntax>().First();
        var nested = outer.Else!.Statement.ShouldBeA<IfStatementSyntax>().And;

        nested.IfKeyword.GetLocation().GetLineSpan().StartLinePosition.Line.ShouldBe(outer.Else.ElseKeyword.GetLocation().GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void Compact_braces_a_body_made_multiline_by_wrapping()
    {
        var result = Format(
            "class C { void M() { if (ready) Run(alpha, beta); } }",
            ("dress_embedded_statement_braces", "compact"),
            ("dress_arguments_layout", "always_multi"));

        Root(result).DescendantNodes().OfType<IfStatementSyntax>().Single().Statement.ShouldBeA<BlockSyntax>();
    }

    [Fact]
    public void Balanced_propagates_a_multiline_body_across_the_chain()
    {
        var result = Format(
            "class C { void M() { if (a) A(); else if (b) B(alpha, beta); else C(); } }",
            ("dress_embedded_statement_braces", "balanced"),
            ("dress_arguments_layout", "always_multi"));
        var chain = Root(result).DescendantNodes().OfType<IfStatementSyntax>().ToArray();

        chain.AllItemsSatisfy(conditional => conditional.Statement.ShouldBeA<BlockSyntax>());
        (chain[0].Else!.Statement is IfStatementSyntax nested ? nested.Else!.Statement : null).ShouldBeA<BlockSyntax>();
    }

    [Fact]
    public void Balanced_propagates_a_multiline_header_across_the_chain()
    {
        var conditional = Root(Format(
                "class C { void M() { if (first && second) A(); else B(); } }",
                ("dress_embedded_statement_braces", "balanced"),
                ("dress_braces_for_multiline_statement_header", "true"),
                ("dress_binary_expressions_layout", "always_multi")))
            .DescendantNodes().OfType<IfStatementSyntax>().Single();

        conditional.Statement.ShouldBeA<BlockSyntax>();
        conditional.Else!.Statement.ShouldBeA<BlockSyntax>();
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Multiline_header_can_contribute_a_brace_constraint(string preference, bool expectedBlock)
    {
        var result = Format(
            "class C { void M() { if (first && second) Run(); } }",
            ("dress_embedded_statement_braces", "compact"),
            ("dress_braces_for_multiline_statement_header", preference),
            ("dress_binary_expressions_layout", "always_multi"));
        var body = Root(result).DescendantNodes().OfType<IfStatementSyntax>().Single().Statement;

        (body is BlockSyntax).ShouldBe(expectedBlock);
    }

    [Fact]
    public void Header_preference_does_not_remove_unconstrained_existing_braces()
    {
        var body = Root(Format(
                "class C { void M() { if (ready) { Run(); } } }",
                ("dress_braces_for_multiline_statement_header", "true")))
            .DescendantNodes().OfType<IfStatementSyntax>().Single().Statement;

        body.ShouldBeA<BlockSyntax>();
    }

    [Fact]
    public void Do_trailing_while_is_part_of_the_statement_header()
    {
        var body = Root(Format(
                "class C { void M() { do Run(); while (first && second); } }",
                ("dress_embedded_statement_braces", "compact"),
                ("dress_braces_for_multiline_statement_header", "true"),
                ("dress_binary_expressions_layout", "always_multi")))
            .DescendantNodes().OfType<DoStatementSyntax>().Single().Statement;

        body.ShouldBeA<BlockSyntax>();
    }

    [Fact]
    public void Nested_body_lines_constrain_the_owning_body()
    {
        var loop = Root(Format(
                "class C { void M() { while (ready) if (other) Run(); } }",
                ("dress_embedded_statement_placement", "next_line"),
                ("dress_embedded_statement_braces", "compact")))
            .DescendantNodes().OfType<WhileStatementSyntax>().Single();

        loop.Statement.ShouldBeA<BlockSyntax>();
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Next_line_uses_the_source_line_ending(string newline)
    {
        var result = Format(
            """
            class C
            {
                void M()
                {
                    if (ready) Run();
                }
            }
            """.ReplaceLineEndings(newline),
            ("dress_embedded_statement_placement", "next_line"),
            ("csharp_indent_block_contents", "true"));
        var conditional = Root(result).DescendantNodes().OfType<IfStatementSyntax>().Single();

        Gap(result, conditional.CloseParenToken, conditional.Statement.GetFirstToken()).ShouldBe($"{newline}            ");
    }

    [Theory]
    [InlineData("compact", false)]
    [InlineData("balanced", false)]
    [InlineData("always", true)]
    public void Empty_bodies_use_the_mode_canonical_form(string mode, bool expectedBlock)
    {
        var body = Root(Format(
                "class C { void M() { if (ready) { } } }",
                ("dress_embedded_statement_braces", mode)))
            .DescendantNodes().OfType<IfStatementSyntax>().Single().Statement;

        (body is BlockSyntax).ShouldBe(expectedBlock);
        (body is EmptyStatementSyntax).ShouldBe(!expectedBlock);
    }

    [Fact]
    public void Meaningful_trivia_preserves_the_owned_boundary()
    {
        Format(
                "class C { void M() { if (ready) /* keep */ Run(); } }",
                ("dress_embedded_statement_placement", "next_line"))
            .ShouldBe("class C { void M() { if (ready) /* keep */ Run(); } }");
    }

    [Fact]
    public void Significant_trivia_prevents_brace_removal()
    {
        Format(
                "class C { void M() { if (/* keep */ ready) { Run(); } } }",
                ("dress_embedded_statement_braces", "compact"))
            .ShouldBe("class C { void M() { if (/* keep */ ready) { Run(); } } }");
    }

    [Fact]
    public void Malformed_occurrences_are_unchanged()
    {
        Format(
                "class C { void M() { if (ready       Run(); } }",
                ("dress_embedded_statement_placement", "next_line"))
            .ShouldBe("class C { void M() { if (ready       Run(); } }");
    }

    [Fact]
    public void Malformed_occurrences_in_a_rewritten_member_are_unchanged()
    {
        // Rewriting the safe conditional must not vouch for the malformed one beside it.
        Format(
                "class C { void M() { if (\n) Work(); if (ok) { Other(); } } }",
                ("dress_embedded_statement_braces", "balanced"),
                ("dress_braces_for_multiline_statement_header", "true"))
            .ShouldBe("class C { void M() { if (\n) Work(); if (ok) Other(); } }");
    }

    [Fact]
    public void Unset_preferences_make_no_change()
    {
        Format(
                "class C { void M() { if (ready)       { Run(); } } }",
                ("dress_embedded_statement_placement", "unset"),
                ("dress_embedded_statement_braces", "unset"),
                ("dress_braces_for_multiline_statement_header", "unset"))
            .ShouldBe("class C { void M() { if (ready)       { Run(); } } }");
    }

    [Fact]
    public void Default_preferences_format_the_regression_and_are_idempotent()
    {
        var preferences = PreferenceCatalog.Defaults
            .Select(preference => (preference.Key.ToName(), preference.Default))
            .ToArray();
        var first = Format("""
            class C
            {
                bool Equal(C right)
                {
                    if (!right.Try(key, out var other)
                        || !value.Equals(other))             return false;
                    return true;
                }
            }
            """,
            preferences);
        first.ShouldBe("""
            class C
            {
                bool Equal(C right)
                {
                    if (!right.Try(key, out var other)
                        || !value.Equals(other))
                    {
                        return false;
                    }
                    return true;
                }
            }
            """);
        Format(first, preferences).ShouldBe(first);
    }

    static CompilationUnitSyntax Root(string source) =>
        CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();

    static string Gap(string source, Microsoft.CodeAnalysis.SyntaxToken left, Microsoft.CodeAnalysis.SyntaxToken right) =>
        source[left.Span.End..right.SpanStart];

}
