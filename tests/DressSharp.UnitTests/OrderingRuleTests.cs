using Xunit;
using EasyAssertions;
using static DressSharp.UnitTests.EmitterTestHarness;

namespace DressSharp.UnitTests;

public class OrderingRuleTests
{
    [Fact]
    public void Orders_global_using_directives_first()
    {
        var first = Format(
            """
            using A;
            global using B;

            """,
            ("dress_global_using_order", "first"));
        first.ShouldBe("""
            global using B;
            using A;

            """);
        Format(first, ("dress_global_using_order", "first")).ShouldBe(first);
    }

    [Fact]
    public void Orders_using_kinds()
    {
        var first = Format(
            """
            using X = A;
            using static B;
            using C;

            """,
            ("dress_using_kind_order", "ordinary,static,alias"));
        first.ShouldBe("""
            using C;
            using static B;
            using X = A;

            """);
        Format(first, ("dress_using_kind_order", "ordinary,static,alias")).ShouldBe(first);
    }

    [Fact]
    public void Modifier_order_is_configurable()
    {
        const string order = "public,protected,internal,private,file,new,static,abstract,virtual,sealed,override,readonly,unsafe,required,volatile,async";
        var result = Format(
            "class C { readonly public static int A; }",
            ("csharp_preferred_modifier_order", order));

        result.ShouldBe("class C { public static readonly int A; }");
    }

    [Fact]
    public void Ordering_stops_at_comments_and_directives()
    {
        Format(
            """
                using Z;
                // boundary
                global using A;
                #if X
                using X;
                #endif

                """,
            ("dress_global_using_order", "first"))
            .ShouldBe("""
                using Z;
                // boundary
                global using A;
                #if X
                using X;
                #endif

                """);
    }
}
