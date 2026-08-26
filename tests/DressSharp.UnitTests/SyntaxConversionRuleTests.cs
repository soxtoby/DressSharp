using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Text.RegularExpressions;
using Xunit;
using EasyAssertions;

namespace DressSharp.UnitTests;

public class SyntaxConversionRuleTests
{
    [Theory]
    [InlineData("dress_method_body", "class C { int M() { return 1; } }", "class C { int M() => 1; }")]
    [InlineData("dress_constructor_body", "class C { C() { Start(); } void Start() {} }", "class C { C() => Start(); void Start() {} }")]
    [InlineData("dress_operator_body", "class C { public static C operator +(C x, C y) { return x; } }", "class C { public static C operator +(C x, C y) => x; }")]
    [InlineData("dress_property_body", "class C { int P { get { return 1; } } }", "class C { int P => 1; }")]
    [InlineData("dress_indexer_body", "class C { int this[int i] { get { return i; } } }", "class C { int this[int i] => i; }")]
    [InlineData("dress_accessor_body", "class C { int P { get { return 1; } } }", "class C { int P { get => 1; } }")]
    [InlineData("dress_lambda_body", "class C { System.Func<int> F = () => { return 1; }; }", "class C { System.Func<int> F = () => 1; }")]
    public void Converts_block_bodies_to_expressions(string key, string source, string expected) =>
        AssertEquivalent(expected, Transform(source, (key, "expression")));

    [Theory]
    [InlineData("dress_method_body", "class C { int M() => 1; }", "class C { int M() { return 1; } }")]
    [InlineData("dress_constructor_body", "class C { C() => Start(); void Start() {} }", "class C { C() { Start(); } void Start() {} }")]
    [InlineData("dress_operator_body", "class C { public static C operator +(C x, C y) => x; }", "class C { public static C operator +(C x, C y) { return x; } }")]
    [InlineData("dress_property_body", "class C { int P => 1; }", "class C { int P { get { return 1; } } }")]
    [InlineData("dress_indexer_body", "class C { int this[int i] => i; }", "class C { int this[int i] { get { return i; } } }")]
    [InlineData("dress_accessor_body", "class C { int P { get => 1; } }", "class C { int P { get { return 1; } } }")]
    public void Converts_expression_bodies_to_blocks(string key, string source, string expected) =>
        AssertEquivalent(expected, Transform(source, (key, "block")));

    [Fact]
    public void Converts_namespace_forms_both_directions()
    {
        AssertEquivalent("namespace N; using X; class C {}", Transform("namespace N { using X; class C {} }", ("dress_namespace_style", "file_scoped")));
        AssertEquivalent("namespace N { using X; class C {} }", Transform("namespace N; using X; class C {}", ("dress_namespace_style", "block_scoped")));
    }

    [Theory]
    [InlineData("always", "if (a) A(); else if (b) B(); else C();", "if (a) { A(); } else if (b) { B(); } else { C(); }")]
    [InlineData("compact", "if (a) { A(); } else if (b) { B(); } else { C(); }", "if (a) A(); else if (b) B(); else C();")]
    [InlineData("balanced", "if (a) { A(); } else if (b) { B(); C(); } else { D(); }", "if (a) { A(); } else if (b) { B(); C(); } else { D(); }")]
    [InlineData("balanced", "if (a) { A(); } else { B(); }", "if (a) A(); else B();")]
    public void Converts_complete_conditional_chains(string preference, string source, string expected) =>
        AssertEquivalent(expected, Transform($"class C {{ void M() {{ {source} }} }}", ("dress_embedded_statement_braces", preference)), wrap: true);

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

    static void AssertEquivalent(string expected, string actual, bool wrap = false)
    {
        if (wrap) expected = $"class C {{ void M() {{ {expected} }} }}";
        Normalize(actual).ShouldBe(Normalize(expected));
        CSharpSyntaxTree.ParseText(actual, cancellationToken: TestContext.Current.CancellationToken).GetDiagnostics(TestContext.Current.CancellationToken).ShouldBeEmpty();
    }

    static string Normalize(string source) => Regex.Replace(CSharpSyntaxTree.ParseText(source).GetRoot().NormalizeWhitespace().ToFullString(), @"\s+", " ").Trim();
}
