using Xunit;

namespace DressSharp.UnitTests;

public sealed class InitializerIndentationEmitterTests
{
    [Fact]
    public void Initializer_setting_preserves_unrelated_source_indentation()
    {
        const string source = "class C\n  {\n      C M() => new C\n        {\n        X = 1\n        };\n          int X;\n  }";
        const string expected = "class C\n  {\n      C M() => new C\n          {\n        X = 1\n          };\n          int X;\n  }";

        Assert.Equal(expected, Format(source, ("dress_object_initializer_indentation", "indented")));
    }

    [Theory]
    [InlineData(
        "dress_object_initializer_indentation",
        "class C\n{\nC M() => new C\n{\nX = 1\n};\nint X;\n}")]
    [InlineData(
        "dress_collection_initializer_indentation",
        "class C\n{\nList<int> M() => new List<int>\n{\n1\n};\n}")]
    [InlineData(
        "dress_array_initializer_indentation",
        "class C\n{\nint[] M() => new[]\n{\n1\n};\n}")]
    [InlineData(
        "dress_with_initializer_indentation",
        "record C(int X)\n{\nC M() => this with\n{\nX = 1\n};\n}")]
    [InlineData(
        "dress_collection_expression_indentation",
        "class C\n{\nint[] M() =>\n[\n1\n];\n}")]
    public void Configures_each_initializer_kind(string key, string source)
    {
        var indented = Format(source, (key, "indented"));
        var notIndented = Format(source, (key, "not_indented"));
        var opening = key == "dress_collection_expression_indentation" ? '[' : '{';
        var closing = key == "dress_collection_expression_indentation" ? ']' : '}';

        Assert.Contains($"\n    {opening}\n", indented);
        Assert.Contains($"\n    {closing};", indented);
        Assert.Contains($"\n{opening}\n", notIndented);
        Assert.Contains($"\n{closing};", notIndented);
        Assert.Equal(indented, Format(indented, (key, "indented")));
        Assert.Equal(notIndented, Format(notIndented, (key, "not_indented")));
    }

    [Theory]
    [InlineData("dress_object_initializer_indentation")]
    [InlineData("dress_collection_initializer_indentation")]
    [InlineData("dress_array_initializer_indentation")]
    [InlineData("dress_with_initializer_indentation")]
    [InlineData("dress_collection_expression_indentation")]
    public void Missing_unset_empty_and_single_line_preferences_add_no_opinion(string key)
    {
        const string source = """
            record C(int X)
            {
            C ObjectSingle = new C { X = 1 };
            C ObjectEmpty = new C
            {
            };
            List<int> CollectionSingle = new List<int> { 1 };
            List<int> CollectionEmpty = new List<int>
            {
            };
            int[] ArraySingle = new[] { 1 };
            int[] ArrayEmpty = new[]
            {
            };
            C WithSingle() => this with { X = 1 };
            C WithEmpty() => this with
            {
            };
            int[] ExpressionSingle = [1];
            int[] ExpressionEmpty =
            [
            ];
            }
            """;
        var baseline = Format(source);

        Assert.Equal(baseline, Format(source, (key, "unset")));
        Assert.Equal(baseline, Format(source, (key, "indented")));
        Assert.Equal(baseline, Format(source, (key, "not_indented")));
    }

    [Fact]
    public void Different_initializer_kinds_keep_independent_settings()
    {
        const string source = "class C\n{\nC M() => new C\n{\nValues = new[]\n{\n1\n}\n};\nint[] Values = [];\n}";

        var result = Format(
            source,
            ("dress_object_initializer_indentation", "indented"),
            ("dress_array_initializer_indentation", "not_indented"));

        Assert.Contains("new C\n    {", result);
        Assert.Contains("new[]\n{", result);
        Assert.Contains("\n}\n    };", result);
        Assert.Equal(result, Format(
            result,
            ("dress_object_initializer_indentation", "indented"),
            ("dress_array_initializer_indentation", "not_indented")));
    }

    [Fact]
    public void Composes_with_syntax_wrapping_and_new_line_rules()
    {
        const string source = "class C { C M() => new C { X = 1, Y = 2 }; int X; int Y; }";

        var result = Format(
            source,
            ("dress_initializers_layout", "always_multi"),
            ("csharp_new_line_before_open_brace", "object_collection_array_initializers"),
            ("dress_object_initializer_indentation", "indented"));

        Assert.Contains("new C\n    {\n", result);
        Assert.Contains("\n    };", result);
        Assert.Equal(result, Format(
            result,
            ("dress_initializers_layout", "always_multi"),
            ("csharp_new_line_before_open_brace", "object_collection_array_initializers"),
            ("dress_object_initializer_indentation", "indented")));
    }

    [Fact]
    public void Syntax_wrapping_preserves_comment_before_indented_closing_delimiter()
    {
        const string source = "class C\n{\nC M() => new C\n{\nX = 1\n/* keep */ };\nint X;\n}";
        (string, string)[] preferences =
        [
            ("dress_initializers_layout", "always_multi"),
            ("dress_object_initializer_indentation", "indented")
        ];

        var result = Format(source, preferences);

        Assert.Contains("/* keep */", result);
        Assert.Contains("/* keep */    };", result);
        Assert.Equal(1, result.Split("/* keep */").Length - 1);
        Assert.Equal(result, Format(result, preferences));
    }

    [Fact]
    public void Uses_tabs_and_preserves_crlf()
    {
        const string source = "class C\r\n{\r\nC M() => new C\r\n{\r\nX = 1\r\n};\r\nint X;\r\n}";

        var result = Format(
            source,
            ("indent_style", "tab"),
            ("csharp_indent_block_contents", "true"),
            ("dress_object_initializer_indentation", "indented"));

        Assert.Contains("new C\r\n\t\t{\r\n", result);
        Assert.Contains("\r\n\t\t};", result);
        Assert.DoesNotContain("\n", result.Replace("\r\n", ""));
    }

    [Fact]
    public void Preserves_comments_and_skips_directive_or_malformed_initializers()
    {
        const string commented = "class C\n{\nC M() => new C\n{\n// keep\nX = 1\n};\nint X;\n}";
        const string directive = "class C\n{\nC M() => new C\n{\n#if true\nX = 1\n#endif\n};\nint X;\n}";
        const string malformed = "class C\n{\nC M() => new C\n{\nX =\n};\nint X;\n}";

        var commentedResult = Format(commented, ("dress_object_initializer_indentation", "indented"));

        Assert.Contains("// keep", commentedResult);
        Assert.Equal(Format(directive), Format(directive, ("dress_object_initializer_indentation", "indented")));
        Assert.Equal(Format(malformed), Format(malformed, ("dress_object_initializer_indentation", "indented")));
        Assert.Equal(commentedResult, Format(commentedResult, ("dress_object_initializer_indentation", "indented")));
    }

    static string Format(string source, params (string Key, string Value)[] preferences) =>
        EmitterTestHarness.Format(source, preferences);
}
