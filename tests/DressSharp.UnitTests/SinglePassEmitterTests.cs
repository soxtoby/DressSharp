using Xunit;

namespace DressSharp.UnitTests;

public class SinglePassEmitterTests
{
    [Fact]
    public void Orders_modifiers_on_the_detached_member_root()
    {
        const string source = "class C { static public int M() => 1; }";
        var result = Emit(source,
            ("csharp_preferred_modifier_order", "public,protected,internal,private,file,new,static,abstract,virtual,sealed,override,readonly,unsafe,required,volatile,async"));
        Assert.Contains("public static int M()", result);
    }

    [Theory]
    [InlineData("csharp_new_line_before_open_brace", "all", "class C { }", "C\n{")]
    [InlineData("csharp_new_line_before_else", "true", "class C { void M() { if (true) { } else { } } }", "}\nelse")]
    [InlineData("csharp_new_line_before_catch", "true", "class C { void M() { try { } catch { } } }", "}\ncatch")]
    [InlineData("csharp_new_line_before_finally", "true", "class C { void M() { try { } finally { } } }", "}\nfinally")]
    [InlineData("csharp_new_line_before_members_in_object_initializers", "true", "class C { int P; int Q; object M() => new C { P = 1, Q = 2 }; }", ",\nQ")]
    [InlineData("csharp_new_line_before_members_in_anonymous_types", "true", "class C { object M() => new { P = 1, Q = 2 }; }", ",\nQ")]
    [InlineData("csharp_new_line_between_query_expression_clauses", "true", "class C { object M(int[] xs) => from x in xs where x > 0 select x; }", "xs\nwhere")]
    public void Dispatches_each_newline_rule_by_its_token_trigger(
        string key,
        string value,
        string source,
        string expected) =>
        Assert.Contains(expected, Emit(source, (key, value)).Replace("\r\n", "\n"));

    [Fact]
    public void Orders_using_lists_in_nested_namespaces()
    {
        const string source = "namespace A { using Zoo; using System; namespace B { using Zoo; using System.Text; class C { } } }";
        var result = Emit(source, ("dotnet_sort_system_directives_first", "true"));
        Assert.Contains("namespace A { using System; using Zoo;", result);
        Assert.Contains("namespace B { using System.Text; using Zoo;", result);
    }

    static string Emit(string source, params (string Key, string Value)[] preferences)
        => EmitterTestHarness.Format(source, preferences);
}
