using DressSharp.Architecture;
using DressSharp.Configuration;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using EasyAssertions;

namespace DressSharp.UnitTests;

public class SyntaxConversionRuleTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void File_scoped_namespace_unindents_first_member_with_default_preferences(string newline)
    {
        var preferences = PreferenceCatalog.Defaults
            .Select(preference => (preference.Key.ToName(), preference.Default))
            .ToArray();
        var source = $"namespace Example{newline}{{{newline}    public class Receipt;{newline}}}";
        var result = Transform(source, preferences);
        result.ShouldBe($"namespace Example;{newline}{newline}public class Receipt;");
        Transform(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("dress_method_body", "class C { int M() { return 1; } }", "class C { int M() => 1; }")]
    [InlineData("dress_constructor_body", "class C { C() { Start(); } void Start() {} }", "class C { C() => Start(); void Start() {} }")]
    [InlineData("dress_operator_body", "class C { public static C operator +(C x, C y) { return x; } }", "class C { public static C operator +(C x, C y) => x; }")]
    [InlineData("dress_property_body", "class C { int P { get { return 1; } } }", "class C { int P => 1; }")]
    [InlineData("dress_indexer_body", "class C { int this[int i] { get { return i; } } }", "class C { int this[int i] => i; }")]
    [InlineData("dress_accessor_body", "class C { int P { get { return 1; } } }", "class C { int P { get => 1; } }")]
    [InlineData("dress_lambda_body", "class C { System.Func<int> F = () => { return 1; }; }", "class C { System.Func<int> F = () => 1; }")]
    public void Converts_block_bodies_to_expressions(string key, string source, string expected) =>
        AssertFormatted(expected, Transform(source, (key, "expression")));

    [Theory]
    [InlineData("dress_method_body", "class C { int M() => 1; }", "class C { int M() {\nreturn 1;\n} }")]
    [InlineData("dress_constructor_body", "class C { C() => Start(); void Start() {} }", "class C { C() {\nStart();\n} void Start() {} }")]
    [InlineData("dress_operator_body", "class C { public static C operator +(C x, C y) => x; }", "class C { public static C operator +(C x, C y) {\nreturn x;\n} }")]
    [InlineData("dress_property_body", "class C { int P => 1; }", "class C { int P {get{\nreturn 1;\n}} }")]
    [InlineData("dress_indexer_body", "class C { int this[int i] => i; }", "class C { int this[int i] {get{\nreturn i;\n}} }")]
    [InlineData("dress_accessor_body", "class C { int P { get => 1; } }", "class C { int P { get {\nreturn 1;\n} } }")]
    public void Converts_expression_bodies_to_blocks(string key, string source, string expected) =>
        AssertFormatted(expected, Transform(source, (key, "block")));

    [Fact]
    public void Converts_namespace_forms_both_directions()
    {
        AssertFormatted("namespace N; using X; class C {}", Transform("namespace N { using X; class C {} }", ("dress_namespace_style", "file_scoped")));
        AssertFormatted("namespace N{using X; class C {}}", Transform("namespace N; using X; class C {}", ("dress_namespace_style", "block_scoped")));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void File_scoped_namespace_semicolon_follows_name(string newline)
    {
        var source = $"namespace Foo{newline}{{{newline}}}";
        var result = Transform(source, ("dress_namespace_style", "file_scoped"));
        result.ShouldBe("namespace Foo;");
        Transform(result, ("dress_namespace_style", "file_scoped")).ShouldBe(result);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void File_scoped_namespace_keeps_contents_after_semicolon(string newline)
    {
        var source = $"namespace Foo.Bar  {newline}  {{{newline}    using X;{newline}    class C {{ }}{newline}}}";
        var result = Transform(source, ("dress_namespace_style", "file_scoped"));
        result.ShouldBe($"namespace Foo.Bar;{newline}    using X;{newline}    class C {{ }}");
        Transform(result, ("dress_namespace_style", "file_scoped")).ShouldBe(result);
    }

    [Theory]
    [InlineData("always", "if (a) A(); else if (b) B(); else C();", "if (a) {\nA();\n} else if (b) {\nB();\n} else {\nC();\n}")]
    [InlineData("compact", "if (a) { A(); } else if (b) { B(); } else { C(); }", "if (a) A(); else if (b) B(); else C();")]
    [InlineData("balanced", "if (a) { A(); } else if (b) { B(); C(); } else { D(); }", "if (a) {\nA();\n} else if (b) { B(); C(); } else {\nD();\n}")]
    [InlineData("balanced", "if (a) { A(); } else { B(); }", "if (a) A(); else B();")]
    public void Converts_complete_conditional_chains(string preference, string source, string expected) =>
        AssertFormatted(expected, Transform($"class C {{ void M() {{ {source} }} }}", ("dress_embedded_statement_braces", preference)), wrap: true);

    [Fact]
    public void Unsafe_occurrences_are_skipped_while_safe_occurrences_continue()
    {
        var result = Transform("class C { int Safe() { return 1; } int Commented() { /* keep */ return 2; } int Broken() { return ; } int Later() { return 3; } }", ("dress_method_body", "expression"));
        result.ShouldBe("class C { int Safe() => 1; int Commented() { /* keep */ return 2; } int Broken() { return ; } int Later() => 3; }");
    }

    [Fact]
    public void Directives_and_dangling_else_prevent_only_unsafe_brace_removal()
    {
        var result = Transform("""
            class C { void M() { if (a) { if (b) A(); } else { B(); } if (c) {
            #if X
             C();
            #endif
            } if (d) { D(); } } }
            """, ("dress_embedded_statement_braces", "compact"));
        result.ShouldBe("""
            class C { void M() { if (a) { if (b) A(); } else B(); if (c) {
            #if X
             C();
            #endif
            } if (d) D(); } }
            """);
    }

    [Theory]
    [InlineData("compact")]
    [InlineData("balanced")]
    [InlineData("always")]
    public void Outer_else_stays_with_outer_if_after_nested_else_if(string mode)
    {
        const string source = """
            if (true) {
                if (true) {
                    Console.WriteLine();
                } else if (false) {
                    Console.WriteLine();
                }
            } else {
                Console.WriteLine();
            }
            """;
        var preferences = new[] { ("dress_embedded_statement_braces", mode) };
        var result = Transform(source, preferences);
        var root = CSharpSyntaxTree.ParseText(result, cancellationToken: TestContext.Current.CancellationToken).GetRoot(TestContext.Current.CancellationToken);
        var outer = root.DescendantNodes().OfType<IfStatementSyntax>().First();
        Assert.NotNull(outer.Else);
        Transform(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("if (b) B(); else if (c) C(); else if (d) D();", true)]
    [InlineData("while (b) { if (c) C(); }", true)]
    [InlineData("for (;;) { if (c) C(); }", true)]
    [InlineData("foreach (var item in items) { if (c) C(); }", true)]
    [InlineData("foreach (var (x, y) in items) { if (c) C(); }", true)]
    [InlineData("using (resource) { if (c) C(); }", true)]
    [InlineData("lock (gate) { if (c) C(); }", true)]
    [InlineData("fixed (int* p = values) { if (c) C(); }", true)]
    [InlineData("if (b) B(); else while (c) { if (d) D(); }", true)]
    [InlineData("if (b) B(); else C();", false)]
    [InlineData("while (b) { if (c) C(); D(); }", false)]
    [InlineData("do { if (c) C(); } while (b);", false)]
    public void Brace_removal_preserves_else_binding_through_embedded_statements(string body, bool needsBraces)
    {
        foreach (var mode in new[] { "compact", "balanced" })
        {
            var preferences = new[] { ("dress_embedded_statement_braces", mode) };
            var result = Transform($"if (a) {{ {body} }} else E();", preferences);
            var tree = CSharpSyntaxTree.ParseText(result, cancellationToken: TestContext.Current.CancellationToken);
            tree.GetDiagnostics(TestContext.Current.CancellationToken).ShouldBeEmpty();
            var outer = tree.GetRoot(TestContext.Current.CancellationToken).DescendantNodes().OfType<IfStatementSyntax>().First();
            Assert.NotNull(outer.Else);
            (outer.Statement is BlockSyntax).ShouldBe(needsBraces);
            Transform(result, preferences).ShouldBe(result);
        }
    }

    [Fact]
    public void Built_in_rules_are_independently_selectable_deterministic_and_idempotent()
    {
        var preferences = new[] { ("dress_method_body", "expression"), ("dress_property_body", "expression"), ("dress_namespace_style", "file_scoped"), ("dress_embedded_statement_braces", "balanced") };
        var first = Transform("namespace N { class C { int P { get { return 1; } } int M() { return P; } void V() { if (P > 0) { M(); } else { V(); } } } }", preferences);
        var second = Transform(first, preferences);
        second.ShouldBe(first);
        CSharpSyntaxTree.ParseText(first, cancellationToken: TestContext.Current.CancellationToken).GetDiagnostics(TestContext.Current.CancellationToken).ShouldBeEmpty();
    }

    static string Transform(string source, params (string Key, string Value)[] preferences)
        => EmitterTestHarness.Format(source, preferences);

    static void AssertFormatted(string expected, string actual, bool wrap = false)
    {
        if (wrap)
            expected = $"class C {{ void M() {{ {expected} }} }}";
        actual.ShouldBe(expected);
        CSharpSyntaxTree.ParseText(actual, cancellationToken: TestContext.Current.CancellationToken).GetDiagnostics(TestContext.Current.CancellationToken).ShouldBeEmpty();
    }
}
