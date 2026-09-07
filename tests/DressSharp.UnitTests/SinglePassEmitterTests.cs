using Xunit;
using EasyAssertions;
using static DressSharp.UnitTests.EmitterTestHarness;

namespace DressSharp.UnitTests;

public class SinglePassEmitterTests
{
    [Theory]
    [InlineData("    public class Receipt;", "public class Receipt;")]
    [InlineData("    using System;\n    public class Receipt;", "using System;\npublic class Receipt;")]
    [InlineData("    extern alias X;\n    public class Receipt;", "extern alias X;\npublic class Receipt;")]
    [InlineData("    [Obsolete]\n    public class Receipt;", "[Obsolete]\npublic class Receipt;")]
    [InlineData("    public class Receipt\n    {\n        int value;\n    }\n    public class Other;", "public class Receipt\n{\n    int value;\n}\npublic class Other;")]
    public void File_scoped_namespace_contents_obey_indentation(string sourceContents, string expectedContents)
    {
        var source = $"namespace Example;\n{sourceContents}";
        var result = Format(source, ("csharp_indent_block_contents", "true"));
        result.ShouldBe($"namespace Example;\n{expectedContents}");
        Format(result, ("csharp_indent_block_contents", "true")).ShouldBe(result);
        Format(source).ShouldBe(source);
    }

    [Fact]
    public void Orders_modifiers_on_the_detached_member_root()
    {
        Format(
                "class C { static public int M() => 1; }",
                ("csharp_preferred_modifier_order", "public,protected,internal,private,file,new,static,abstract,virtual,sealed,override,readonly,unsafe,required,volatile,async"))
            .ShouldBe("class C { public static int M() => 1; }");
    }

    [Fact]
    public void Malformed_sibling_does_not_suppress_emitter_rules_in_rewritten_member()
    {
        Format(
                "class C { int BrokenNam = ; static public int M() => 1+2; }",
                ("csharp_preferred_modifier_order", "public,protected,internal,private,file,new,static,abstract,virtual,sealed,override,readonly,unsafe,required,volatile,async"),
                ("csharp_space_around_binary_operators", "before_and_after"))
            .ShouldBe("class C { int BrokenNam = ; public static int M() => 1 + 2; }");
    }

    [Fact]
    public void Generated_expression_body_owns_spaces_around_its_arrow()
    {
        Format("""
            class C
            {
                int M(int value)
                {
                    return value;
                }
            }
            """,
            ("dress_method_body", "expression"))
            .ShouldBe("""
            class C
            {
                int M(int value) => value;
            }
            """);
    }

    [Fact]
    public void Generated_void_expression_body_discards_statement_indentation()
    {
        Format("""
            sealed class SystemInteractiveBrowser : IInteractiveBrowser
            {
                public void Open(Uri address)
                {
                    Process.Start(new ProcessStartInfo(address.AbsoluteUri)
                    {
                        UseShellExecute = true
                    });
                }
            }
            """,
            ("dress_method_body", "expression"))
            .ShouldBe("""
            sealed class SystemInteractiveBrowser : IInteractiveBrowser
            {
                public void Open(Uri address) => Process.Start(new ProcessStartInfo(address.AbsoluteUri)
                    {
                        UseShellExecute = true
                    });
            }
            """);
    }

    [Fact]
    public void Adds_newlines_before_open_braces()
    {
        AssertNewlineRule(
            ("csharp_new_line_before_open_brace", "all"),
            "class C { }",
            """
            class C
            { }
            """);
    }

    [Fact]
    public void Adds_newlines_before_else()
    {
        AssertNewlineRule(
            ("csharp_new_line_before_else", "true"),
            "class C { void M() { if (true) { } else { } } }",
            """
            class C { void M() { if (true) { }
            else { } } }
            """);
    }

    [Fact]
    public void Adds_newlines_before_catch()
    {
        AssertNewlineRule(
            ("csharp_new_line_before_catch", "true"),
            "class C { void M() { try { } catch { } } }",
            """
            class C { void M() { try { }
            catch { } } }
            """);
    }

    [Fact]
    public void Adds_newlines_before_finally()
    {
        AssertNewlineRule(
            ("csharp_new_line_before_finally", "true"),
            "class C { void M() { try { } finally { } } }",
            """
            class C { void M() { try { }
            finally { } } }
            """);
    }

    [Fact]
    public void Adds_newlines_between_object_initializer_members()
    {
        AssertNewlineRule(
            ("csharp_new_line_before_members_in_object_initializers", "true"),
            "class C { int P; int Q; object M() => new C { P = 1, Q = 2 }; }",
            """
            class C { int P; int Q; object M() => new C { P = 1,
            Q = 2 }; }
            """);
    }

    [Fact]
    public void Adds_newlines_between_anonymous_type_members()
    {
        AssertNewlineRule(
            ("csharp_new_line_before_members_in_anonymous_types", "true"),
            "class C { object M() => new { P = 1, Q = 2 }; }",
            """
            class C { object M() => new { P = 1,
            Q = 2 }; }
            """);
    }

    [Fact]
    public void Adds_newlines_between_query_clauses()
    {
        AssertNewlineRule(
            ("csharp_new_line_between_query_expression_clauses", "true"),
            "class C { object M(int[] xs) => from x in xs where x > 0 select x; }",
            """
            class C { object M(int[] xs) => from x in xs
            where x > 0
            select x; }
            """);
    }

    [Fact]
    public void Orders_using_lists_in_nested_namespaces()
    {
        var result = Format("namespace A { using Zoo; using System; namespace B { using Zoo; using System.Text; class C { } } }", ("dotnet_sort_system_directives_first", "true"));
        result.ShouldBe("namespace A { using System; using Zoo; namespace B { using System.Text; using Zoo; class C { } } }");
    }

    static void AssertNewlineRule(
        (string Key, string Value) preference,
        string source,
        string expected)
    {
        var result = Format(source, preference);
        result.ShouldBe(expected);
        Format(result, preference).ShouldBe(expected);
    }
}
