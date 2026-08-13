using DressSharp.Architecture;
using DressSharp.Rules;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DressSharp.UnitTests;

public class ConstructLayoutRuleTests
{
    public static TheoryData<string, string> Constructs => new()
    {
        { "dress_arguments_layout", "class C { void M() { N(alpha, beta); } void N(int a, int b) {} }" },
        { "dress_parameters_layout", "class C { void M(int alpha, string beta) {} }" },
        { "dress_initializers_layout", "class C { object M() => new C { A = 1, B = 2 }; int A; int B; }" },
        { "dress_collection_expressions_layout", "class C { int[] M() => [alpha, beta]; int alpha; int beta; }" },
        { "dress_base_type_lists_layout", "class C : Alpha, IBeta {} class Alpha {} interface IBeta {}" },
        { "dress_constraint_clauses_layout", "class C<T, U> where T : class where U : struct {}" },
        { "dress_member_access_chains_layout", "class C { string M() => value.Trim().ToString(); string value = \"\"; }" },
        { "dress_binary_expressions_layout", "class C { bool M() => alpha && beta; bool alpha; bool beta; }" },
        { "dress_conditional_expressions_layout", "class C { int M() => condition ? alpha : beta; bool condition; int alpha; int beta; }" },
        { "dress_query_clauses_layout", "class C { object M(int[] xs) => from x in xs where x > 0 select x; }" },
        { "dress_attributes_layout", "[A, B] class C {} class AAttribute : System.Attribute {} class BAttribute : System.Attribute {}" }
    };

    [Theory]
    [MemberData(nameof(Constructs))]
    public void Supports_all_layout_modes_and_is_idempotent(string key, string source)
    {
        var single = Transform(source, (key, "always_single"));
        var multi = Transform(source, (key, "always_multi"));
        var automaticSingle = Transform(source, (key, "auto"), ("max_line_length", "500"));
        var automaticMulti = Transform(source, (key, "auto"), ("max_line_length", "1"));

        Assert.Equal(single, automaticSingle);
        Assert.Equal(multi, automaticMulti);
        Assert.NotEqual(single, multi);
        Assert.Equal(multi, Transform(multi, (key, "always_multi")));
        Assert.Equal(single, Transform(multi, (key, "always_single")));
    }

    [Fact]
    public void Auto_with_max_line_length_off_chooses_single_line()
    {
        const string source = "class C { void M(int alpha, int beta) {} }";
        Assert.Equal(
            Transform(source, ("dress_parameters_layout", "always_single")),
            Transform(source, ("dress_parameters_layout", "auto"), ("max_line_length", "off")));
    }

    [Fact]
    public void Directives_block_only_the_affected_occurrence()
    {
        const string source = "class C { void A(int a, int b) {}\n#if X\nvoid B(int a, int b) {}\n#endif\n}";
        var result = Transform(source, ("dress_parameters_layout", "always_multi"));
        Assert.Contains("void A(\n", result);
        Assert.Contains("void B(int a, int b)", result);
    }

    [Fact]
    public void Attached_comments_are_preserved_with_owned_arguments()
    {
        const string source = "class C { void M() { N(alpha, /* beta */ beta); } void N(int a, int b) {} int alpha; int beta; }";
        var result = Transform(source, ("dress_arguments_layout", "always_multi"));
        Assert.Contains("/* beta */", result);
        Assert.Equal(result, Transform(result, ("dress_arguments_layout", "always_multi")));
    }

    [Fact]
    public void Familiar_construct_preferences_are_auto_and_share_the_maximum()
    {
        string[] keys =
        [
            "dress_arguments_layout", "dress_parameters_layout", "dress_initializers_layout",
            "dress_collection_expressions_layout", "dress_base_type_lists_layout", "dress_constraint_clauses_layout",
            "dress_member_access_chains_layout", "dress_binary_expressions_layout", "dress_conditional_expressions_layout",
            "dress_query_clauses_layout", "dress_attributes_layout"
        ];
        Assert.All(keys, key => Assert.Equal("auto", Configuration.PreferenceCatalog.Familiar[key]));
        Assert.Equal("180", Configuration.PreferenceCatalog.Familiar["max_line_length"]);
    }

    [Fact]
    public void Nested_rules_compose_in_catalog_order()
    {
        const string source = "class C { void M() { N(alpha + beta, gamma); } void N(int a, int b) {} int alpha; int beta; int gamma; }";
        var preferences = new[]
        {
            ("dress_arguments_layout", "always_multi"),
            ("dress_binary_expressions_layout", "always_multi")
        };
        var result = Transform(source, preferences);
        Assert.Contains("N(\n", result);
        Assert.Contains("\n        + beta", result);
        Assert.Equal(result, Transform(result, preferences));
    }

    static string Transform(string source, params (string Key, string Value)[] preferences)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        var configuration = new FormattingConfiguration(preferences.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)));
        var result = new TransformationPipeline(RuleCatalog.BuiltIn).Transform(root, configuration);
        Assert.True(result.Succeeded, result.Failure?.ToString());
        return result.Root.ToFullString();
    }
}
