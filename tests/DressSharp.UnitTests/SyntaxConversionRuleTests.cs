using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Text.RegularExpressions;
using Xunit;

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
        const string block = "namespace N { using X; class C {} }";
        const string file = "namespace N; using X; class C {}";
        AssertEquivalent(file, Transform(block, ("dress_namespace_style", "file_scoped")));
        AssertEquivalent(block, Transform(file, ("dress_namespace_style", "block_scoped")));
    }

    [Theory]
    [InlineData("always", "if (a) A(); else if (b) B(); else C();", "if (a) { A(); } else if (b) { B(); } else { C(); }")]
    [InlineData("compact", "if (a) { A(); } else if (b) { B(); } else { C(); }", "if (a) A(); else if (b) B(); else C();")]
    [InlineData("balanced", "if (a) { A(); } else if (b) { B(); C(); } else { D(); }", "if (a) { A(); } else if (b) { B(); C(); } else { D(); }")]
    [InlineData("balanced", "if (a) { A(); } else { B(); }", "if (a) A(); else B();")]
    public void Converts_complete_conditional_chains(string preference, string source, string expected) =>
        AssertEquivalent(expected, Transform($"class C {{ void M() {{ {source} }} }}", ("dress_conditional_braces", preference)), wrap: true);

    [Fact]
    public void Unsafe_occurrences_are_skipped_while_safe_occurrences_continue()
    {
        const string source = "class C { int Safe() { return 1; } int Commented() { /* keep */ return 2; } int Broken() { return ; } int Later() { return 3; } }";
        var result = Transform(source, ("dress_method_body", "expression"));
        Assert.Contains("Safe() => 1;", Normalize(result));
        Assert.Contains("Commented() { /* keep */ return 2; }", Normalize(result));
        Assert.Contains("Broken() { return; }", Normalize(result));
        Assert.Contains("Later() => 3;", Normalize(result));
    }

    [Fact]
    public void Directives_and_dangling_else_prevent_only_unsafe_brace_removal()
    {
        const string source = "class C { void M() { if (a) { if (b) A(); } else { B(); } if (c) {\n#if X\n C();\n#endif\n} if (d) { D(); } } }";
        var result = Normalize(Transform(source, ("dress_conditional_braces", "compact")));
        Assert.Contains("if (a) { if (b) A(); } else B();", result);
        Assert.Contains("#if X", result);
        Assert.Contains("if (d) D();", result);
    }

    [Fact]
    public void Built_in_rules_are_independently_selectable_deterministic_and_idempotent()
    {
        const string source = "namespace N { class C { int P { get { return 1; } } int M() { return P; } void V() { if (P > 0) { M(); } else { V(); } } } }";
        var preferences = new[] { ("dress_method_body", "expression"), ("dress_property_body", "expression"), ("dress_namespace_style", "file_scoped"), ("dress_conditional_braces", "balanced") };
        var first = Transform(source, preferences);
        var second = Transform(first, preferences);
        Assert.Equal(first, second);
        Assert.Empty(CSharpSyntaxTree.ParseText(first, cancellationToken: TestContext.Current.CancellationToken).GetDiagnostics(TestContext.Current.CancellationToken));
    }

    static string Transform(string source, params (string Key, string Value)[] preferences)
        => EmitterTestHarness.Format(source, preferences);

    static void AssertEquivalent(string expected, string actual, bool wrap = false)
    {
        if (wrap) expected = $"class C {{ void M() {{ {expected} }} }}";
        Assert.Equal(Normalize(expected), Normalize(actual));
        Assert.Empty(CSharpSyntaxTree.ParseText(actual, cancellationToken: TestContext.Current.CancellationToken).GetDiagnostics(TestContext.Current.CancellationToken));
    }

    static string Normalize(string source) => Regex.Replace(CSharpSyntaxTree.ParseText(source).GetRoot().NormalizeWhitespace().ToFullString(), @"\s+", " ").Trim();
}
