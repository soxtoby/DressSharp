using Xunit;

namespace DressSharp.UnitTests;

public class OrderingRuleTests
{
    [Theory]
    [InlineData("dress_global_using_order", "first", "using A;\nglobal using B;\n", "global using B;\nusing A;")]
    [InlineData("dress_using_kind_order", "ordinary,static,alias", "using X = A;\nusing static B;\nusing C;\n", "using C;\nusing static B;\nusing X = A;")]
    public void Orders_using_directives(string key, string value, string source, string expected)
    {
        var first = EmitterTestHarness.Format(source, (key, value));
        Assert.Contains(expected, first.Replace("\r\n", "\n"));
        Assert.Equal(first, EmitterTestHarness.Format(first, (key, value)));
    }

    [Fact]
    public void Modifier_order_is_configurable()
    {
        const string order = "public,protected,internal,private,file,new,static,abstract,virtual,sealed,override,readonly,unsafe,required,volatile,async";
        var result = EmitterTestHarness.Format(
            "class C { readonly public static int A; }",
            ("csharp_preferred_modifier_order", order));

        Assert.Contains("public static readonly", result);
    }

    [Fact]
    public void Ordering_stops_at_comments_and_directives()
    {
        const string source = "using Z;\n// boundary\nglobal using A;\n#if X\nusing X;\n#endif\n";
        Assert.Equal(source, EmitterTestHarness.Format(source, ("dress_global_using_order", "first")));
    }
}
