using DressSharp.Architecture;
using DressSharp.Rules;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DressSharp.UnitTests;

public class MicrosoftCompatibilityRuleTests
{
    [Theory]
    [InlineData("csharp_space_after_cast", "true", "class C { int M(object x) => (int)x; }", "(int) x")]
    [InlineData("csharp_space_after_cast", "false", "class C { int M(object x) => (int) x; }", "(int)x")]
    [InlineData("csharp_space_after_keywords_in_control_flow_statements", "true", "class C { void M() { if(true) { } } }", "if (true)")]
    [InlineData("csharp_space_after_keywords_in_control_flow_statements", "false", "class C { void M() { if (true) { } } }", "if(true)")]
    [InlineData("csharp_space_before_colon_in_inheritance_clause", "false", "class C : B { } class B { }", "C: B")]
    [InlineData("csharp_space_after_colon_in_inheritance_clause", "false", "class C : B { } class B { }", "C :B")]
    [InlineData("csharp_space_around_binary_operators", "before_and_after", "class C { int M() => 1+2; }", "1 + 2")]
    [InlineData("csharp_space_around_binary_operators", "none", "class C { int M() => 1 + 2; }", "1+2")]
    [InlineData("csharp_space_around_binary_operators", "ignore", "class C { int M() => 1  +  2; }", "1  +  2")]
    [InlineData("csharp_space_between_method_declaration_parameter_list_parentheses", "true", "class C { void M(int x) { } }", "M( int x )")]
    [InlineData("csharp_space_between_method_declaration_empty_parameter_list_parentheses", "true", "class C { void M() { } }", "M( )")]
    [InlineData("csharp_space_between_method_declaration_name_and_open_parenthesis", "true", "class C { void M() { } }", "M ()")]
    [InlineData("csharp_space_between_method_call_parameter_list_parentheses", "true", "class C { void M() { N(1); } void N(int x) { } }", "N( 1 )")]
    [InlineData("csharp_space_between_method_call_empty_parameter_list_parentheses", "true", "class C { void M() { N(); } void N() { } }", "N( )")]
    [InlineData("csharp_space_between_method_call_name_and_opening_parenthesis", "true", "class C { void M() { N(); } void N() { } }", "N ()")]
    [InlineData("csharp_space_after_comma", "false", "class C { void M(int x, int y) { } }", "x,int")]
    [InlineData("csharp_space_before_comma", "true", "class C { void M(int x, int y) { } }", "x , int")]
    [InlineData("csharp_space_after_dot", "true", "class C { string M() => this.ToString(); }", "this. ToString")]
    [InlineData("csharp_space_before_dot", "true", "class C { string M() => this.ToString(); }", "this .ToString")]
    [InlineData("csharp_space_after_semicolon_in_for_statement", "false", "class C { void M() { for (int i = 0; i < 1; i++) { } } }", "0;i")]
    [InlineData("csharp_space_before_semicolon_in_for_statement", "true", "class C { void M() { for (int i = 0; i < 1; i++) { } } }", "0 ; i")]
    [InlineData("csharp_space_around_declaration_statements", "false", "class C { void M() { int x = 1; } }", "x=1")]
    [InlineData("csharp_space_before_open_square_brackets", "true", "class C { int M(int[] x) => x[0]; }", "x [0]")]
    [InlineData("csharp_space_between_empty_square_brackets", "true", "class C { int[] x; }", "int[ ]")]
    [InlineData("csharp_space_between_square_brackets", "true", "class C { int M(int[] x) => x[0]; }", "x[ 0 ]")]
    public void Applies_spacing_value(string key, string value, string source, string expected)
    {
        var first = Transform(source, (key, value));
        Assert.Contains(expected, first);
        Assert.Equal(first, Transform(first, (key, value)));
    }

    [Theory]
    [InlineData("false", "( true )", "(true)")]
    [InlineData("control_flow_statements", "(true)", "( true )")]
    [InlineData("expressions", "(1 + 2)", "( 1 + 2 )")]
    [InlineData("type_casts", "(int)x", "( int )x")]
    public void Applies_parenthesis_categories(string value, string fragment, string expected)
    {
        var source = fragment.Contains("true") ? $"class C {{ void M() {{ if {fragment} {{ }} }} }}" : fragment.Contains("int") ? $"class C {{ int M(object x) => {fragment}; }}" : $"class C {{ int M() => {fragment}; }}";
        Assert.Contains(expected, Transform(source, ("csharp_space_between_parentheses", value)));
    }

    [Fact]
    public void Sorts_and_separates_system_usings()
    {
        const string source = "using Zoo;\nusing System.Text;\nusing Alpha;\n";
        var sorted = Transform(source, ("dotnet_sort_system_directives_first", "true"));
        Assert.StartsWith("using System.Text;\nusing Zoo;", sorted);
        Assert.Contains("using System.Text;\n\nusing Zoo;", Transform(sorted, ("dotnet_separate_import_directive_groups", "true")));
    }

    [Theory]
    [InlineData("csharp_preserve_single_line_blocks", "true", "class C { void M() { int x = 1; } }", "{ int x = 1; }")]
    [InlineData("csharp_preserve_single_line_blocks", "false", "class C { void M() { int x = 1; } }", "{\nint x = 1; \n}")]
    [InlineData("csharp_preserve_single_line_statements", "true", "class C { void M() { int x = 1; int y = 2; } }", "int x = 1; int y")]
    [InlineData("csharp_preserve_single_line_statements", "false", "class C { void M() { int x = 1; int y = 2; } }", "int x = 1;\nint y")]
    public void Applies_single_line_preservation(string key, string value, string source, string expected)
    {
        var first = Transform(source, (key, value));
        Assert.Contains(expected, first.Replace("\r\n", "\n"));
        Assert.Equal(first, Transform(first, (key, value)));
    }

    [Theory]
    [InlineData("csharp_space_after_cast")]
    [InlineData("csharp_preserve_single_line_blocks")]
    [InlineData("dotnet_sort_system_directives_first")]
    [InlineData("dotnet_separate_import_directive_groups")]
    public void Missing_and_unset_preferences_preserve_source(string key)
    {
        const string source = "using Zoo;\nusing System;\nclass C { int M(object x) => (int) x; }";
        Assert.Equal(source, Transform(source));
        Assert.Equal(source, Transform(source, (key, "unset")));
    }

    [Fact]
    public void Comments_directives_and_malformed_occurrences_are_preserved()
    {
        const string source = "class C { int M() => 1 /* keep */  +  2;\n#if X\nint N() => 1  +  2;\n#endif\nint P() => (1 + ; }";
        var result = Transform(source, ("csharp_space_around_binary_operators", "none"));
        Assert.Contains("1 /* keep */  +  2", result);
        Assert.Contains("#if X\nint N() => 1  +  2;\n#endif", result);
        Assert.Contains("(1 + ;", result);
    }

    static string Transform(string source, params (string Key, string Value)[] preferences)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        var result = new TransformationPipeline(RuleCatalog.BuiltIn).Transform(root,
            new FormattingConfiguration(preferences.Select(x => new KeyValuePair<string, string>(x.Key, x.Value))));
        Assert.True(result.Succeeded, result.Failure?.ToString());
        return result.Root.ToFullString();
    }
}
