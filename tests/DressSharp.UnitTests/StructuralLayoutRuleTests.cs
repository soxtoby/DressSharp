using DressSharp.Architecture;
using DressSharp.Rules;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DressSharp.UnitTests;

public class StructuralLayoutRuleTests
{
    [Theory]
    [InlineData("dress_blank_lines_between_members", "0", "class C\n{\n    int A;\n\n\n    int B;\n}\n", "int A;\n    int B;")]
    [InlineData("dress_max_consecutive_blank_lines", "1", "class C\n{\n\n\n\n    int A;\n}\n", "{\n\n    int A;")]
    [InlineData("dress_line_comment_spacing", "none", "class C { int A; // note\n int B; }", "//note\n")]
    [InlineData("dress_block_comment_spacing", "single", "class C { /*note*/ int A; }", "/* note */ int A;")]
    [InlineData("dress_attached_comment_placement", "own_line", "class C { /* note */ int A; }", "/* note */\nint A;")]
    [InlineData("dress_global_using_order", "first", "using A;\nglobal using B;\n", "global using B;\nusing A;")]
    [InlineData("dress_using_kind_order", "ordinary,static,alias", "using X = A;\nusing static B;\nusing C;\n", "using C;\nusing static B;\nusing X = A;")]
    [InlineData("dress_object_initializer_indentation", "indented", "class C { object M() => new C\n{\n    A = 1\n}; int A; }", "new C\n    {")]
    [InlineData("dress_collection_expression_indentation", "not_indented", "class C { int[] M() =>\n    [\n        1\n    ]; }", "=>\n[\n")]
    public void Applies_structural_layout_rule(string key, string value, string source, string expected)
    {
        var first = Transform(source, (key, value));
        Assert.Contains(expected, first.Replace("\r\n", "\n"));
        Assert.Equal(first, Transform(first, (key, value)));
    }

    [Fact]
    public void Modifier_order_is_configurable()
    {
        const string key = "csharp_preferred_modifier_order";
        const string order = "public,protected,internal,private,file,new,static,abstract,virtual,sealed,override,readonly,unsafe,required,volatile,async";
        Assert.Contains("public static readonly", Transform("class C { readonly public static int A; }", (key, order)));
    }

    [Theory]
    [InlineData("dress_blank_lines_around_namespaces")]
    [InlineData("dress_xml_comment_placement")]
    [InlineData("dress_array_initializer_indentation")]
    public void Missing_and_unset_structural_preferences_preserve_source(string key)
    {
        const string source = "namespace N { class C { int[] A = new[] { 1 }; } }";
        Assert.Equal(source, Transform(source));
        Assert.Equal(source, Transform(source, (key, "unset")));
    }

    [Fact]
    public void Ordering_stops_at_comments_and_directives()
    {
        const string source = "using Z;\n// boundary\nglobal using A;\n#if X\nusing X;\n#endif\n";
        Assert.Equal(source, Transform(source, ("dress_global_using_order", "first")));
    }

    [Theory]
    [InlineData("dress_xml_comment_placement", "separated", "/// <summary>Text</summary>\nclass C {}", "/// <summary>Text</summary>\n\nclass C")]
    [InlineData("dress_xml_comment_placement", "attached", "/// <summary>Text</summary>\n\nclass C {}", "/// <summary>Text</summary>\nclass C")]
    [InlineData("dress_xml_element_layout", "multi_line", "/// <summary>Text</summary>\nclass C {}", "/// <summary>\n/// Text\n/// </summary>")]
    [InlineData("dress_xml_element_layout", "single_line", "/// <summary>\n/// Text\n/// </summary>\nclass C {}", "/// <summary>Text</summary>")]
    public void Applies_xml_comment_layout(string key, string value, string source, string expected)
    {
        var first = Transform(source, (key, value)).Replace("\r\n", "\n");
        Assert.Contains(expected, first);
        Assert.Equal(first, Transform(first, (key, value)).Replace("\r\n", "\n"));
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
