using EasyAssertions;
using Xunit;
using static DressSharp.UnitTests.EmitterTestHarness;

namespace DressSharp.UnitTests;

public sealed class InitializerIndentationEmitterTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Non_indented_collection_expression_arguments_keep_the_argument_indent(string lineEnding)
    {
        var source = """
            class C
            {
                Result M()
                {
                    return new(
                        [.. spacing],
                        [.. newLines],
                        triggers);
                }
            }
            """;
        source = source.Replace("\n", lineEnding);

        var result = Format(source,
                ("dress_collection_expression_argument_indentation", "not_indented"),
                ("csharp_indent_block_contents", "true"));

        result.ShouldBe(source);
        Format(result,
                ("dress_collection_expression_argument_indentation", "not_indented"),
                ("csharp_indent_block_contents", "true"))
            .ShouldBe(result);
    }

    [Theory]
    [InlineData("Process(\n", ");")]
    [InlineData("Process(values:\n", ");")]
    [InlineData("new Container(\n", ");")]
    [InlineData("Process((\n", "));")]
    [InlineData("Process((string[])\n", ");")]
    public void Collection_expression_arguments_have_independent_indentation(string prefix, string suffix)
    {
        var source = "class C\n{\nvoid M()\n{\n" + prefix + "[\n\"first\"\n]" + suffix + "\n}\n}";
        var expected = "class C\n{\nvoid M()\n{\n" + prefix + "    [\n\"first\"\n    ]" + suffix + "\n}\n}";

        Format(source, ("dress_collection_expression_indentation", "indented")).ShouldBe(source);
        var result = Format(source,
            ("dress_collection_expression_indentation", "not_indented"),
            ("dress_collection_expression_argument_indentation", "indented"));
        result.ShouldBe(expected);
        Format(result, ("dress_collection_expression_argument_indentation", "indented")).ShouldBe(result);
        Format(result, ("dress_collection_expression_argument_indentation", "not_indented")).ShouldBe(source);
        Format(source, ("dress_collection_expression_argument_indentation", "unset")).ShouldBe(source);
    }

    [Theory]
    [InlineData("static readonly string[] PropertyNames =")]
    [InlineData("string[] Values { get; } =")]
    [InlineData("string[] M() =>")]
    [InlineData("void M() { string[] values =", "; }")]
    [InlineData("void M() { Values =", "; }")]
    [InlineData("string[] M() { return", "; }")]
    [InlineData("void M() { Process(() =>", "); }")]
    public void Collection_expressions_outside_arguments_have_independent_indentation(string prefix, string suffix = ";")
    {
        var source = "class C\n{\n" + prefix + "\n[\n\"first\"\n]" + suffix + "\n}";
        var expected = source.Replace("\n[", "\n    [").Replace("\n]", "\n    ]");

        Format(source, ("dress_collection_expression_argument_indentation", "indented")).ShouldBe(source);
        var result = Format(source,
            ("dress_collection_expression_indentation", "indented"),
            ("dress_collection_expression_argument_indentation", "not_indented"));
        result.ShouldBe(expected);
        Format(result, ("dress_collection_expression_indentation", "indented")).ShouldBe(result);
    }

    [Fact]
    public void Nested_initializer_uses_the_indent_from_a_planned_parent_line()
    {
        Format("""
                class C
                {
                    object M() => N(first, [1, 2]);
                }
                """,
                ("dress_arguments_layout", "always_multi"),
                ("dress_collection_expression_argument_indentation", "not_indented"),
                ("csharp_indent_block_contents", "true"))
            .ShouldBe("""
                class C
                {
                    object M() => N(
                        first,
                        [1, 2]
                    );
                }
                """);
    }

    [Fact]
    public void Auto_layout_keeps_nested_initializer_items_stable()
    {
        (string, string)[] preferences =
        [
            ("max_line_length", "180"),
            ("dress_object_initializer_layout", "auto"),
            ("dress_collection_initializer_layout", "auto"),
            ("csharp_new_line_before_open_brace", "all"),
            ("csharp_indent_block_contents", "true"),
            ("dress_object_initializer_indentation", "indented"),
            ("dress_collection_initializer_indentation", "indented")
        ];

        var result = Format("""
            class C
            {
                void M()
                {
                    var value = new C("git") { ArgumentList = { "-C", "directory", "ls-files", "--cached", "--others", "--exclude-standard", "-z" }, StandardOutputEncoding = Encoding.UTF8, Environment = { ["LANG"] = "C", ["LC_ALL"] = "C" } };
                }
            }
            """,
            preferences);

        result.ShouldBe("""
            class C
            {
                void M()
                {
                    var value = new C("git")
                        {
                            ArgumentList = { "-C", "directory", "ls-files", "--cached", "--others", "--exclude-standard", "-z" },
                            StandardOutputEncoding = Encoding.UTF8,
                            Environment = { ["LANG"] = "C", ["LC_ALL"] = "C" }
                        };
                }
            }
            """);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Initializer_setting_preserves_unrelated_source_indentation()
    {
        Format("""
            class C
              {
                  C M() => new C
                    {
                    X = 1
                    };
                      int X;
              }
            """,
            ("dress_object_initializer_indentation", "indented")).ShouldBe("""
            class C
              {
                  C M() => new C
                      {
                    X = 1
                      };
                      int X;
              }
            """);
    }

    [Fact]
    public void Configures_object_initializer_indentation()
    {
        const string key = "dress_object_initializer_indentation";
        var indented = Format("""
            class C
            {
            C M() => new C
            {
            X = 1
            };
            int X;
            }
            """,
            (key, "indented"));
        indented.ShouldBe("""
            class C
            {
            C M() => new C
                {
            X = 1
                };
            int X;
            }
            """);
        var notIndented = Format(indented, (key, "not_indented"));
        notIndented.ShouldBe("""
            class C
            {
            C M() => new C
            {
            X = 1
            };
            int X;
            }
            """);
        Format(indented, (key, "indented")).ShouldBe(indented);
        Format(notIndented, (key, "not_indented")).ShouldBe(notIndented);
    }

    [Fact]
    public void Configures_nested_anonymous_object_initializer_indentation()
    {
        (string, string)[] preferences =
        [
            ("csharp_indent_block_contents", "true"),
            ("dress_object_initializer_indentation", "indented")
        ];

        var result = Format("""
            class C
            {
                object M() => new
                {
                    Value = 1,
                    Nested = new
                    {
                        Value = 2
                    }
                };
            }
            """,
            preferences);

        result.ShouldBe("""
            class C
            {
                object M() => new
                    {
                        Value = 1,
                        Nested = new
                            {
                                Value = 2
                            }
                    };
            }
            """);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Configures_collection_initializer_indentation()
    {
        const string key = "dress_collection_initializer_indentation";
        var indented = Format("""
            class C
            {
            List<int> M() => new List<int>
            {
            1
            };
            }
            """,
            (key, "indented"));
        indented.ShouldBe("""
            class C
            {
            List<int> M() => new List<int>
                {
            1
                };
            }
            """);
        var notIndented = Format(indented, (key, "not_indented"));
        notIndented.ShouldBe("""
            class C
            {
            List<int> M() => new List<int>
            {
            1
            };
            }
            """);
        Format(indented, (key, "indented")).ShouldBe(indented);
        Format(notIndented, (key, "not_indented")).ShouldBe(notIndented);
    }

    [Fact]
    public void Configures_array_initializer_indentation()
    {
        const string key = "dress_array_initializer_indentation";
        var indented = Format("""
            class C
            {
            int[] M() => new[]
            {
            1
            };
            }
            """,
            (key, "indented"));
        indented.ShouldBe("""
            class C
            {
            int[] M() => new[]
                {
            1
                };
            }
            """);
        var notIndented = Format(indented, (key, "not_indented"));
        notIndented.ShouldBe("""
            class C
            {
            int[] M() => new[]
            {
            1
            };
            }
            """);
        Format(indented, (key, "indented")).ShouldBe(indented);
        Format(notIndented, (key, "not_indented")).ShouldBe(notIndented);
    }

    [Fact]
    public void Configures_with_initializer_indentation()
    {
        const string key = "dress_with_initializer_indentation";
        var indented = Format("""
            record C(int X)
            {
            C M() => this with
            {
            X = 1
            };
            }
            """,
            (key, "indented"));
        indented.ShouldBe("""
            record C(int X)
            {
            C M() => this with
                {
            X = 1
                };
            }
            """);
        var notIndented = Format(indented, (key, "not_indented"));
        notIndented.ShouldBe("""
            record C(int X)
            {
            C M() => this with
            {
            X = 1
            };
            }
            """);
        Format(indented, (key, "indented")).ShouldBe(indented);
        Format(notIndented, (key, "not_indented")).ShouldBe(notIndented);
    }

    [Fact]
    public void Configures_collection_expression_indentation()
    {
        const string key = "dress_collection_expression_indentation";
        var indented = Format("""
            class C
            {
            int[] M() =>
            [
            1
            ];
            }
            """,
            (key, "indented"));
        indented.ShouldBe("""
            class C
            {
            int[] M() =>
                [
            1
                ];
            }
            """);
        var notIndented = Format(indented, (key, "not_indented"));
        notIndented.ShouldBe("""
            class C
            {
            int[] M() =>
            [
            1
            ];
            }
            """);
        Format(indented, (key, "indented")).ShouldBe(indented);
        Format(notIndented, (key, "not_indented")).ShouldBe(notIndented);
    }

    [Theory]
    [InlineData("dress_object_initializer_indentation")]
    [InlineData("dress_collection_initializer_indentation")]
    [InlineData("dress_array_initializer_indentation")]
    [InlineData("dress_with_initializer_indentation")]
    [InlineData("dress_collection_expression_indentation")]
    [InlineData("dress_collection_expression_argument_indentation")]
    public void Missing_unset_empty_and_single_line_preferences_add_no_opinion(string key)
    {
        var baseline = Format("""
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
            """);

        Format("""
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
            """,
            (key, "unset")).ShouldBe(baseline);
        Format("""
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
            """,
            (key, "indented")).ShouldBe(baseline);
        Format("""
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
            """,
            (key, "not_indented")).ShouldBe(baseline);
    }

    [Fact]
    public void Different_initializer_kinds_keep_independent_settings()
    {
        var result = Format(
            """
            class C
            {
            C M() => new C
            {
            Values = new[]
            {
            1
            }
            };
            int[] Values = [];
            }
            """,
            ("dress_object_initializer_indentation", "indented"),
            ("dress_array_initializer_indentation", "not_indented"));

        result.ShouldBe("""
            class C
            {
            C M() => new C
                {
            Values = new[]
            {
            1
            }
                };
            int[] Values = [];
            }
            """);
        Format(
            result,
            ("dress_object_initializer_indentation", "indented"),
            ("dress_array_initializer_indentation", "not_indented")).ShouldBe(result);
    }

    [Fact]
    public void Composes_with_syntax_wrapping_and_new_line_rules()
    {
        var result = Format(
            "class C { C M() => new C { X = 1, Y = 2 }; int X; int Y; }",
            ("dress_object_initializer_layout", "expanded"),
            ("csharp_new_line_before_open_brace", "object_collection_array_initializers"),
            ("dress_object_initializer_indentation", "indented"));

        result.ShouldBe("""
            class C { C M() => new C
                {
                X = 1,
                Y = 2
                }; int X; int Y; }
            """);
        Format(
            result,
            ("dress_object_initializer_layout", "expanded"),
            ("csharp_new_line_before_open_brace", "object_collection_array_initializers"),
            ("dress_object_initializer_indentation", "indented")).ShouldBe(result);
    }

    [Fact]
    public void Syntax_wrapping_preserves_comment_before_indented_closing_delimiter()
    {
        (string, string)[] preferences =
        [
            ("dress_object_initializer_layout", "expanded"),
            ("dress_object_initializer_indentation", "indented")
        ];

        var result = Format("""
            class C
            {
            C M() => new C
            {
            X = 1
            /* keep */ };
            int X;
            }
            """,
            preferences);

        result.ShouldBe("""
            class C
            {
            C M() => new C
                {
                X = 1
            /* keep */    };
            int X;
            }
            """);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Uses_tabs_and_preserves_crlf()
    {
        const string sourceTemplate = """
            class C
            {
            C M() => new C
            {
            X = 1
            };
            int X;
            }
            """;
        const string expectedTemplate = """
            class C
            {
            	C M() => new C
            		{
            			X = 1
            		};
            	int X;
            }
            """;
        var source = sourceTemplate.Replace("\n", "\r\n");
        var expected = expectedTemplate.Replace("\n", "\r\n");

        var result = Format(
            source,
            ("indent_style", "tab"),
            ("csharp_indent_block_contents", "true"),
            ("dress_object_initializer_indentation", "indented"));

        result.ShouldBe(expected);
    }

    [Fact]
    public void Preserves_comments_and_skips_directive_or_malformed_initializers()
    {
        const string commented = """
            class C
            {
            C M() => new C
            {
            // keep
            X = 1
            };
            int X;
            }
            """;

        const string directive = "class C\n{\nC M() => new C\n{\n#if true\nX = 1\n#endif\n};\nint X;\n}";
        const string malformed = "class C\n{\nC M() => new C\n{\nX =\n};\nint X;\n}";

        var commentedResult = Format(commented, ("dress_object_initializer_indentation", "indented"));

        commentedResult.ShouldBe("""
            class C
            {
            C M() => new C
                {
            // keep
            X = 1
                };
            int X;
            }
            """);
        Format(directive, ("dress_object_initializer_indentation", "indented")).ShouldBe(Format(directive));
        Format(malformed, ("dress_object_initializer_indentation", "indented")).ShouldBe(Format(malformed));
        Format(commentedResult, ("dress_object_initializer_indentation", "indented")).ShouldBe(commentedResult);
    }
}
