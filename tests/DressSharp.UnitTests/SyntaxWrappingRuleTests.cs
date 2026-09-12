using DressSharp.Architecture;
using DressSharp.Configuration;
using EasyAssertions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DressSharp.UnitTests;

public class SyntaxWrappingRuleTests
{
    [Theory]
    [InlineData("\n", "MultipleChoice([\n    \"accessors\",\n    \"types\"\n], \"all\", \"none\");")]
    [InlineData("\r\n", "MultipleChoice([\n    \"accessors\",\n    \"types\"\n], \"all\", \"none\");")]
    [InlineData("\n", "MultipleChoice([\"accessors\", \"types\"], \"all\", \"none\");")]
    public void Auto_expands_arguments_when_a_collection_argument_spans_lines(string lineEnding, string source)
    {
        var preferences = new[]
        {
            ("dress_arguments_layout", "auto"),
            ("dress_collection_expressions_layout", "always_multi"),
            ("csharp_indent_block_contents", "true"),
            ("max_line_length", "500")
        };
        var result = Format(source.ReplaceLineEndings(lineEnding), preferences);
        result.ShouldContain(lineEnding + "    \"all\"," + lineEnding + "    \"none\"");
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Auto_separates_items_in_a_multiline_collection_argument(string lineEnding)
    {
        var source = """
            NewLineRule[] newLineRules =
                [
                    new NewLineRule(RuleKey.CSharpNewLineBeforeOpenBrace, "Before open brace", "Newlines",
                          NewLineKind.OpenBrace,
                          RuleValues.MultipleChoice([
                              "accessors", "anonymous_methods", "anonymous_types", "control_blocks", "events", "indexers", "lambdas", "local_functions", "methods",
                              "object_collection_array_initializers", "properties", "types"
                          ], "all", "none"), "all")
                ];
            """.ReplaceLineEndings(lineEnding);
        var preferences = PreferenceCatalog.Defaults.Select(item => (item.Key.ToName(), item.Default)).ToArray();
        var result = Format(source, preferences);
        var root = CSharpSyntaxTree.ParseText(result, cancellationToken: TestContext.Current.CancellationToken)
            .GetRoot(TestContext.Current.CancellationToken);
        var collection = root.DescendantNodes().OfType<CollectionExpressionSyntax>()
            .Single(item => item.Elements.Count == 12);
        var previousLine = collection.OpenBracketToken.GetLocation().GetLineSpan().StartLinePosition.Line;
        foreach (var element in collection.Elements)
        {
            var line = element.GetLocation().GetLineSpan().StartLinePosition.Line;
            (line > previousLine).ShouldBe(true);
            previousLine = line;
        }
        var arguments = ((ArgumentSyntax)collection.Parent!).Parent as ArgumentListSyntax;
        previousLine = arguments!.OpenParenToken.GetLocation().GetLineSpan().StartLinePosition.Line;
        foreach (var argument in arguments.Arguments)
        {
            var span = argument.GetLocation().GetLineSpan();
            (span.StartLinePosition.Line > previousLine).ShouldBe(true);
            previousLine = span.EndLinePosition.Line;
        }
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Auto_normalizes_mixed_collection_items_and_preserves_fitting_single_line_collections(string lineEnding)
    {
        var preferences = new[] { ("dress_collection_expressions_layout", "auto"), ("max_line_length", "160") };
        var source = "int[] values = [1, 2,\n    3];".ReplaceLineEndings(lineEnding);
        var expected = "int[] values = [\n    1,\n    2,\n    3];".ReplaceLineEndings(lineEnding);
        var result = Format(source, preferences);
        result.ShouldBe(expected);
        Format(result, preferences).ShouldBe(result);
        Format("int[] values = [1, 2, 3];", preferences).ShouldBe("int[] values = [1, 2, 3];");
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Auto_aligns_existing_constructor_arguments_with_default_preferences(string lineEnding)
    {
        var source = """
            IFormattingRule[] embeddedStatementRules =
                [
                        new EmbeddedStatementPreferenceRule(
                                RuleKey.DressEmbeddedStatementPlacement, "Control statement body placement", "Embedded statements",
                                ["same_line", "next_line"],
                                "next_line",
                            "the boundary before brace-optional embedded statements",
                            "Only boundary whitespace changes",
                            description: "Place the body of if, else, loops, using, lock, and fixed on the same line or a new line. "
                                + "For example: if (condition) return false;. Applies with or without braces. "
                                + "Embedded statement placement: single-line if, inline return, return new line.",
                            expandedCaption: "Control statement body placement")
                ];
            """.ReplaceLineEndings(lineEnding);
        var preferences = PreferenceCatalog.Defaults.Select(item => (item.Key.ToName(), item.Default)).ToArray();
        var result = Format(source, preferences);
        var root = CSharpSyntaxTree.ParseText(result, cancellationToken: TestContext.Current.CancellationToken)
            .GetRoot(TestContext.Current.CancellationToken);
        var constructor = root.DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>().Single();
        var owner = constructor.GetLocation().GetLineSpan().StartLinePosition;
        var previousLine = owner.Line;
        foreach (var argument in constructor.ArgumentList!.Arguments)
        {
            var position = argument.GetLocation().GetLineSpan().StartLinePosition;
            position.Character.ShouldBe(owner.Character + 4);
            (position.Line > previousLine).ShouldBe(true);
            previousLine = position.Line;
        }
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Auto_separates_constructor_arguments_inside_a_collection(string lineEnding)
    {
        var source = """
            IFormattingRule[] rules =
            [
                new EmbeddedStatementPreferenceRule(
                    RuleKey.DressEmbeddedStatementPlacement, "Control statement body placement", "Embedded statements",
                    ["same_line", "next_line"],
                    "next_line",
                    description: "Place the body on the same line or a new line. "
                        + "Applies with or without braces.",
                    expandedCaption: "Control statement body placement")
            ];
            """.ReplaceLineEndings(lineEnding);
        var expected = source.Replace(
            "RuleKey.DressEmbeddedStatementPlacement, \"Control statement body placement\", \"Embedded statements\",",
            "RuleKey.DressEmbeddedStatementPlacement," + lineEnding
                + "        \"Control statement body placement\"," + lineEnding
                + "        \"Embedded statements\",", StringComparison.Ordinal);
        var preferences = new[]
        {
            ("dress_arguments_layout", "auto"),
            ("dress_collection_expressions_layout", "auto"),
            ("csharp_indent_block_contents", "true"),
            ("max_line_length", "160")
        };
        source = source
            .Replace("        RuleKey.", "            RuleKey.", StringComparison.Ordinal)
            .Replace("        [\"same_line\"", "            [\"same_line\"", StringComparison.Ordinal)
            .Replace("        \"next_line\",", "            \"next_line\",", StringComparison.Ordinal);
        var result = Format(source, preferences);
        result.ShouldBe(expected);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("\n", "dress_arguments_layout", "M(alpha, beta,\n    gamma);", "M(\n    alpha,\n    beta,\n    gamma);")]
    [InlineData("\r\n", "dress_arguments_layout", "M(alpha, beta,\n    gamma);", "M(\n    alpha,\n    beta,\n    gamma);")]
    [InlineData("\n", "dress_arguments_layout", "new C(alpha, beta,\n    gamma);", "new C(\n    alpha,\n    beta,\n    gamma);")]
    [InlineData("\n", "dress_parameters_layout", "void M(int alpha, int beta,\n    int gamma) {}", "void M(\n    int alpha,\n    int beta,\n    int gamma) {}")]
    [InlineData("\r\n", "dress_parameters_layout", "void M(int alpha, int beta,\n    int gamma) {}", "void M(\n    int alpha,\n    int beta,\n    int gamma) {}")]
    public void Auto_puts_each_item_of_a_multiline_list_on_its_own_line(string lineEnding, string key, string source, string expected)
    {
        var preferences = new[] { (key, "auto"), ("max_line_length", "500") };
        var result = Format(source.ReplaceLineEndings(lineEnding), preferences);
        result.ShouldBe(expected.ReplaceLineEndings(lineEnding));
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Auto_binary_wrapping_keeps_fitting_comparisons_together(string lineEnding)
    {
        const string source = """
            static bool IsUnder(string path, string directory) =>
                Path.GetRelativePath(directory, path) is var relative && relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
            """;
        const string expected = """
            static bool IsUnder(string path, string directory) =>
                Path.GetRelativePath(directory, path) is var relative
                && relative != ".."
                && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
            """;
        (string Key, string Value)[] preferences =
        [
            ("dress_binary_expressions_layout", "auto"),
            ("csharp_indent_block_contents", "true"),
            ("max_line_length", "100")
        ];

        var result = Format(source.ReplaceLineEndings(lineEnding), preferences);
        result.ShouldBe(expected.ReplaceLineEndings(lineEnding));
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Auto_binary_wrapping_keeps_coalesce_with_multiline_raw_string_inside_initializer(string lineEnding)
    {
        const string source = """"
            class C(string? example)
            {
                object Metadata { get; } = new
                {
                    Example = example ?? """
                        aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
                        bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
                        cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc
                        """
                };
            }
            """";
        (string Key, string Value)[] preferences =
        [
            ("dress_binary_expressions_layout", "auto"),
            ("max_line_length", "100")
        ];

        var input = source.ReplaceLineEndings(lineEnding);
        var result = Format(input, preferences);
        result.ShouldBe(input);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Auto_binary_wrapping_still_breaks_oversized_comparisons()
    {
        const string source = """
            bool M() => firstCondition && longComparisonOperand != anotherLongComparisonOperand;
            """;
        const string expected = """
            bool M() => firstCondition
                && longComparisonOperand
                    != anotherLongComparisonOperand;
            """;
        (string Key, string Value)[] preferences =
        [
            ("dress_binary_expressions_layout", "auto"),
            ("max_line_length", "45")
        ];

        var result = Format(source, preferences);
        result.ShouldBe(expected);
        Format(result, preferences).ShouldBe(result);
    }

    [InlineData(
        "dress_arguments_layout",
        "class C { void M() { N(alpha, beta); } void N(int a, int b) {} int alpha; int beta; }",
        "class C { void M() { N(alpha, beta); } void N(int a, int b) {} int alpha; int beta; }",
        """
        class C { void M() { N(
            alpha,
            beta
        ); } void N(int a, int b) {} int alpha; int beta; }
        """)]
    [InlineData(
        "dress_parameters_layout",
        "class C { void M(int alpha, string beta) {} }",
        "class C { void M(int alpha, string beta) {} }",
        """
        class C { void M(
            int alpha,
            string beta
        ) {} }
        """)]
    [InlineData(
        "dress_collection_expressions_layout",
        "class C { int[] M() => [alpha, beta]; int alpha; int beta; }",
        "class C { int[] M() => [alpha, beta]; int alpha; int beta; }",
        """
        class C { int[] M() => [
            alpha,
            beta
        ]; int alpha; int beta; }
        """)]
    [InlineData(
        "dress_base_type_lists_layout",
        "class C : Alpha, IBeta {} class Alpha {} interface IBeta {}",
        "class C : Alpha, IBeta {} class Alpha {} interface IBeta {}",
        """
        class C
            : Alpha,
            IBeta {} class Alpha {} interface IBeta {}
        """)]
    [InlineData(
        "dress_constraint_clauses_layout",
        "class C<T, U> where T : class where U : struct {}",
        "class C<T, U> where T : class where U : struct {}",
        """
        class C<T, U>
            where T : class
            where U : struct {}
        """)]
    [InlineData(
        "dress_member_access_chains_layout",
        """class C { string M() => value.Trim().ToString(); string value = ""; }""",
        """class C { string M() => value.Trim().ToString(); string value = ""; }""",
        """
        class C { string M() => value
            .Trim()
            .ToString(); string value = ""; }
        """)]
    [InlineData(
        "dress_binary_expressions_layout",
        "class C { bool M() => alpha && beta; bool alpha; bool beta; }",
        "class C { bool M() => alpha && beta; bool alpha; bool beta; }",
        """
        class C { bool M() => alpha
            && beta; bool alpha; bool beta; }
        """)]
    [InlineData(
        "dress_conditional_expressions_layout",
        "class C { int M() => condition ? alpha : beta; bool condition; int alpha; int beta; }",
        "class C { int M() => condition ? alpha : beta; bool condition; int alpha; int beta; }",
        """
        class C { int M() => condition
            ? alpha
            : beta; bool condition; int alpha; int beta; }
        """)]
    [InlineData(
        "dress_query_clauses_layout",
        "class C { object M(int[] xs) => from x in xs where x > 0 select x; }",
        "class C { object M(int[] xs) => from x in xs where x > 0 select x; }",
        """
        class C { object M(int[] xs) => from x in xs
            where x > 0
            select x; }
        """)]
    [InlineData(
        "dress_attributes_layout",
        "[A, B] class C {} class AAttribute : System.Attribute {} class BAttribute : System.Attribute {}",
        "[A, B] class C {} class AAttribute : System.Attribute {} class BAttribute : System.Attribute {}",
        """
        [
            A,
            B
        ] class C {} class AAttribute : System.Attribute {} class BAttribute : System.Attribute {}
        """)]
    [Theory]
    public void Each_syntax_shape_supports_all_layout_modes_and_is_idempotent(
        string key,
        string source,
        string expectedSingle,
        string expectedMulti)
    {
        var single = Format(source, (key, "always_single"));
        var multi = Format(source, (key, "always_multi"));
        var automaticSingle = Format(source, (key, "auto"), ("max_line_length", "500"));
        var automaticMulti = Format(source, (key, "auto"), ("max_line_length", "1"));
        single.ShouldBe(expectedSingle);
        multi.ShouldBe(expectedMulti);
        automaticSingle.ShouldBe(single);
        automaticMulti.ShouldBe(multi);
        multi.ShouldNotBe(single);
        Format(multi, (key, "always_single")).ShouldBe(single);
        Format(multi, (key, "always_multi")).ShouldBe(multi);
    }

    [Fact]
    public void Auto_wraps_only_after_the_visual_width_exceeds_the_maximum()
    {
        Format("""
                class C
                {
                    void M(int alpha, int beta) {}
                }
                """,
                ("dress_parameters_layout", "auto"),
                ("max_line_length", "31"))
            .ShouldBe(Format("""
                class C
                {
                    void M(int alpha, int beta) {}
                }
                """,
                ("dress_parameters_layout", "always_single")));

        Format("""
                class C
                {
                    void M(int alpha, int beta) {}
                }
                """,
                ("dress_parameters_layout", "auto"),
                ("max_line_length", "30"))
            .ShouldBe(Format("""
                class C
                {
                    void M(int alpha, int beta) {}
                }
                """,
                ("dress_parameters_layout", "always_multi")));
    }

    [Fact]
    public void Auto_with_max_line_length_off_chooses_single_line()
    {
        Format(
                "class C { void M(int alpha, int beta) {} }",
                ("dress_parameters_layout", "auto"),
                ("max_line_length", "off"))
            .ShouldBe(Format(
                "class C { void M(int alpha, int beta) {} }",
                ("dress_parameters_layout", "always_single")));
    }

    [Fact]
    public void Auto_preserves_an_existing_multiline_layout()
    {
        Format(
                """
                class C
                {
                    RuleMetadata Metadata { get; } = new(
                        ruleKey,
                        acceptedValues,
                        ownedSyntax,
                        "Only same-line whitespace changes");
                }
                """,
                ("dress_arguments_layout", "auto"),
                ("max_line_length", "500"))
            .ShouldBe("""
                class C
                {
                    RuleMetadata Metadata { get; } = new(
                        ruleKey,
                        acceptedValues,
                        ownedSyntax,
                        "Only same-line whitespace changes");
                }
                """);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Auto_base_type_list_measures_only_lines_containing_its_boundaries(string lineEnding)
    {
        const string source = """
            sealed class ControlFlowKeywordSpacingRule() : TokenSpacingRule(
                RuleKey.CSharpSpaceAfterKeywordsInControlFlowStatements, "After keywords in control flow statements", null,
                ["true", "false"],
                "true",
                "control-flow keywords")
            { }
            """;
        var expected = source.ReplaceLineEndings(lineEnding);

        var result = Format(
            expected,
            ("dress_base_type_lists_layout", "auto"),
            ("max_line_length", "160"));

        result.ShouldBe(expected);
        Format(
                result,
                ("dress_base_type_lists_layout", "auto"),
                ("max_line_length", "160"))
            .ShouldBe(result);
    }

    [Fact]
    public void Initializer_layout_overrides_the_standard_open_brace_rule()
    {
        const string source = """
            class C
            {
                static readonly HashSet<string> Values = new(StringComparer.OrdinalIgnoreCase) { ".git", ".hg", ".svn" };
            }
            """;

        Format(
                source,
                ("dress_collection_initializer_layout", "compact"),
                ("max_line_length", "500"),
                ("csharp_new_line_before_open_brace", "object_collection_array_initializers"),
                ("csharp_indent_block_contents", "true"),
                ("dress_collection_initializer_indentation", "indented"))
            .ShouldBe("""
                class C {
                    static readonly HashSet<string> Values = new(StringComparer.OrdinalIgnoreCase) { ".git", ".hg", ".svn" };
                }
                """);
    }

    [Fact]
    public void Constraint_auto_measures_the_declaration_header_not_its_body()
    {
        const string source =
            "class C<T> where T : class { string Value => \"this body is deliberately much wider than the header\"; }";

        Format(
                source,
                ("dress_constraint_clauses_layout", "auto"),
                ("max_line_length", "40"))
            .ShouldBe(Format(source, ("dress_constraint_clauses_layout", "always_single")));
    }

    [Fact]
    public void Query_continuations_support_all_layout_modes_recursively()
    {
        var single = Format(
            "class C { object M(int[] xs) => from x in xs group x by x into grouped where grouped.Any() select grouped; }",
            ("dress_query_clauses_layout", "always_single"));
        var multi = Format(
            "class C { object M(int[] xs) => from x in xs group x by x into grouped where grouped.Any() select grouped; }",
            ("dress_query_clauses_layout", "always_multi"));

        single.ShouldBe("class C { object M(int[] xs) => from x in xs group x by x into grouped where grouped.Any() select grouped; }");
        multi.ShouldBe("""
            class C { object M(int[] xs) => from x in xs
                group x by x
                into grouped
                where grouped.Any()
                select grouped; }
            """);
        Format("class C { object M(int[] xs) => from x in xs group x by x into grouped where grouped.Any() select grouped; }",
                ("dress_query_clauses_layout", "auto"),
                ("max_line_length", "500"))
            .ShouldBe(single);
        Format("class C { object M(int[] xs) => from x in xs group x by x into grouped where grouped.Any() select grouped; }",
                ("dress_query_clauses_layout", "auto"),
                ("max_line_length", "1"))
            .ShouldBe(multi);
        Format(multi, ("dress_query_clauses_layout", "always_multi"))
            .ShouldBe(multi);
    }

    [Fact]
    public void Query_continuation_line_comments_prevent_unsafe_compaction()
    {
        const string source =
            "class C { object M(int[] xs) => from x in xs group x by x\n"
            + "into grouped // keep continuation attached\n"
            + "where grouped.Any()\n"
            + "select grouped; }";

        Format(source, ("dress_query_clauses_layout", "always_single"))
            .ShouldBe(Format(source));
    }

    [Fact]
    public void Unsafe_query_continuations_are_left_unchanged()
    {
        const string malformed =
            "class C { object M(int[] xs) => from x in xs group x by x into ; }";
        const string directives =
            "class C { object M(int[] xs) => from x in xs group x by x into grouped\n"
            + "#if X\nwhere grouped.Any()\n#endif\n"
            + "select grouped; }";

        Format(malformed, ("dress_query_clauses_layout", "always_multi")).ShouldBe(Format(malformed));
        Format(directives, ("dress_query_clauses_layout", "always_multi")).ShouldBe(Format(directives));
    }

    [Fact]
    public void Auto_counts_preserved_block_comment_text()
    {
        const string plain = "class C { void M() { N(alpha, beta); } void N(int a, int b) {} int alpha; int beta; }";
        const string commented = "class C { void M() { N(alpha, /* this comment makes the invocation much too wide */ beta); } void N(int a, int b) {} int alpha; int beta; }";

        var preferences = new[]
        {
            ("dress_arguments_layout", "auto"), ("max_line_length", "60")
        };

        Format(plain, preferences).ShouldBe(plain);
        Format(commented, preferences).ShouldBe("""
            class C { void M() { N(
                alpha,
                /* this comment makes the invocation much too wide */ beta
            ); } void N(int a, int b) {} int alpha; int beta; }
            """);
    }

    [Fact]
    public void Auto_measures_spacing_rules_that_run_before_syntax_wrapping()
    {
        var compact = Format(
            "class C { bool M() => alpha&&beta; bool alpha; bool beta; }",
            ("dress_binary_expressions_layout", "auto"),
            ("csharp_space_around_binary_operators", "none"),
            ("max_line_length", "34"));
        var spaced = Format(
            "class C { bool M() => alpha&&beta; bool alpha; bool beta; }",
            ("dress_binary_expressions_layout", "auto"),
            ("csharp_space_around_binary_operators", "before_and_after"),
            ("max_line_length", "34"));

        compact.ShouldBe("class C { bool M() => alpha &&beta; bool alpha; bool beta; }");
        spaced.ShouldBe("""
            class C { bool M() => alpha
                && beta; bool alpha; bool beta; }
            """);
    }

    [Fact]
    public void Absent_and_unset_preferences_leave_syntax_shapes_alone()
    {
        Format("class C { void M(int alpha, int beta) {} }")
            .ShouldBe("class C { void M(int alpha, int beta) {} }");
        Format("class C { void M(int alpha, int beta) {} }",
                ("dress_parameters_layout", "unset"))
            .ShouldBe("class C { void M(int alpha, int beta) {} }");
    }

    [Fact]
    public void Nested_rules_compose_in_one_idempotent_batch()
    {
        var preferences = new[]
        {
            ("dress_arguments_layout", "always_multi"), ("dress_binary_expressions_layout", "always_multi")
        };

        var result = Format(
            "class C { void M() { N(alpha + beta, gamma); } void N(int a, int b) {} int alpha; int beta; int gamma; }",
            preferences);

        result.ShouldBe("""
            class C { void M() { N(
                alpha
                    + beta,
                gamma
            ); } void N(int a, int b) {} int alpha; int beta; int gamma; }
            """);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Outer_initializer_auto_layout_uses_original_position_after_inner_argument_rewrite()
    {
        var preferences = new[]
        {
            ("dress_arguments_layout", "auto"), ("dress_array_initializer_layout", "auto"), ("max_line_length", "20")
        };

        var result = Format("""
            class C
            {
                object[] M() => new[]
                {
                    N(firstArgument, secondArgument)
                };
            }
            """,
            preferences);

        result.ShouldBe("""
            class C
            {
                object[] M() => new[]
                {
                    N(
                        firstArgument,
                        secondArgument
                    )
                };
            }
            """);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("dress_arguments_layout",
        "class C { object M() => Outer(Inner(firstArgument, secondArgument), thirdArgument); }",
        """
        class C { object M() => Outer(
            Inner(
                firstArgument,
                secondArgument
            ),
            thirdArgument
        ); }
        """)]
    [InlineData("dress_collection_expressions_layout",
        "class C { object[] M() => [N(firstArgument, secondArgument), thirdArgument]; }",
        """
        class C { object[] M() => [
            N(
                firstArgument,
                secondArgument
            ),
            thirdArgument
        ]; }
        """)]
    [InlineData("dress_member_access_chains_layout",
        "class C { object M() => N(firstArgument, secondArgument).First.Second; }",
        """
        class C { object M() => N(
                firstArgument,
                secondArgument
            )
            .First
            .Second; }
        """)]
    [InlineData("dress_binary_expressions_layout",
        "class C { int M() => N(firstArgument, secondArgument) + thirdArgument; }",
        """
        class C { int M() => N(
                firstArgument,
                secondArgument
            )
            + thirdArgument; }
        """)]
    [InlineData("dress_conditional_expressions_layout",
        "class C { int M() => N(firstArgument, secondArgument) ? firstArgument : secondArgument; }",
        """
        class C { int M() => N(
                firstArgument,
                secondArgument
            )
            ? firstArgument
            : secondArgument; }
        """)]
    [InlineData("dress_query_clauses_layout",
        "class C { object M(int[] xs) => from x in xs where N(firstArgument, secondArgument) select x; }",
        """
        class C { object M(int[] xs) => from x in xs
            where N(
                firstArgument,
                secondArgument
            )
            select x; }
        """)]
    public void Outer_auto_layout_kinds_keep_stable_source_anchors_after_inner_rewrite(
        string outerKey,
        string source,
        string expected)
    {
        var preferences = outerKey == "dress_arguments_layout"
            ? new[]
            {
                ("dress_arguments_layout", "auto"), ("max_line_length", "20")
            }
            :
            [
                ("dress_arguments_layout", "auto"),
                (outerKey, "auto"),
                ("max_line_length", "20")
            ];

        var result = Format(source, preferences);

        result.ShouldBe(expected);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Layout_plan_uses_member_rewrite_tokens_without_rebuilding_the_tree()
    {
        var preferences = new[]
        {
            ("dress_method_body", "expression"), ("dress_arguments_layout", "auto"), ("max_line_length", "20")
        };

        var result = Format("class C { object M() { return N(firstArgument, secondArgument); } }", preferences);

        result.ShouldBe("""
            class C { object M() => N(
                firstArgument,
                secondArgument
            ); }
            """);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Member_rewrite_inside_constrained_type_does_not_inherit_original_layout_ancestor()
    {
        var preferences = new[]
        {
            ("dress_method_body", "expression"), ("dress_constraint_clauses_layout", "always_single"), ("dress_arguments_layout", "always_multi")
        };

        var result = Format(
            "class PaddingWithLongEnoughNameToMoveTheFollowingDeclarationFarAway { } class C<T> where T : class { object M() { return N(firstArgument, secondArgument); } }",
            preferences);

        result.ShouldBe("""
            class PaddingWithLongEnoughNameToMoveTheFollowingDeclarationFarAway { } class C<T> where T : class { object M() => N(
                firstArgument,
                secondArgument
            ); }
            """);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Auto_in_rewritten_member_uses_the_original_insertion_column_for_nested_syntax_shapes()
    {
        var preferences = new[]
        {
            ("dress_method_body", "expression"), ("dress_arguments_layout", "auto"), ("max_line_length", "50")
        };

        var result = Format("class AClassNameLongEnoughToMatter { object M() { return N(first, second); } }", preferences);

        result.ShouldBe("""
            class AClassNameLongEnoughToMatter { object M() => N(
                first,
                second
            ); }
            """);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Auto_keeps_distinct_anchors_for_two_rewritten_members()
    {
        var preferences = new[]
        {
            ("dress_method_body", "expression"), ("dress_arguments_layout", "auto"), ("max_line_length", "55")
        };

        var result = Format("class C { object A() { return N(alpha, beta); } object B() { return N(alpha, beta); } }", preferences);

        result.ShouldBe("""
            class C { object A() => N(alpha, beta); object B() => N(
                alpha,
                beta
            ); }
            """);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Auto_uses_effective_prefix_spacing_at_the_exact_threshold()
    {
        var maximum = "class C { void M() { if(true) N(alpha, beta); } }".IndexOf("N(", StringComparison.Ordinal)
            + "N(alpha, beta)".Length;

        var compact = Format(
            "class C { void M() { if(true) N(alpha, beta); } }",
            ("dress_arguments_layout", "auto"),
            ("csharp_space_after_keywords_in_control_flow_statements", "false"),
            ("max_line_length", maximum.ToString()));
        var spaced = Format(
            "class C { void M() { if(true) N(alpha, beta); } }",
            ("dress_arguments_layout", "auto"),
            ("csharp_space_after_keywords_in_control_flow_statements", "true"),
            ("max_line_length", maximum.ToString()));

        compact.ShouldBe("class C { void M() { if(true) N(alpha, beta); } }");

        spaced.ShouldBe("""
            class C { void M() { if (true) N(
                alpha,
                beta
            ); } }
            """);
        Format(
            spaced,
            ("dress_arguments_layout", "auto"),
            ("csharp_space_after_keywords_in_control_flow_statements", "true"),
            ("max_line_length", maximum.ToString())).ShouldBe(spaced);
    }

    [Fact]
    public void Auto_uses_effective_prefix_spacing_inside_a_rewritten_member()
    {
        var maximum = "class C { int M() => prefix+".Length + "N(alpha, beta)".Length;
        var common = new[]
        {
            ("dress_method_body", "expression"), ("dress_arguments_layout", "auto"), ("max_line_length", maximum.ToString())
        };

        var compact = Format("class C { int M() { return prefix+N(alpha, beta); } }", [.. common, ("csharp_space_around_binary_operators", "none")]);
        var spaced = Format("class C { int M() { return prefix+N(alpha, beta); } }", [.. common, ("csharp_space_around_binary_operators", "before_and_after")]);

        compact.ShouldBe("class C { int M() => prefix+N(alpha, beta); }");
        spaced.ShouldBe("""
            class C { int M() => prefix + N(
                alpha,
                beta
            ); }
            """);
        Format(
            spaced,
            [.. common, ("csharp_space_around_binary_operators", "before_and_after")]).ShouldBe(spaced);
    }

    [Fact]
    public void Auto_member_access_does_not_claim_short_chains_inside_arguments()
    {
        var preferences = new[]
        {
            ("dress_member_access_chains_layout", "auto"), ("max_line_length", "80")
        };

        var result = Format(
            "class C { object M(string preference) => preference.Split(',', StringSplitOptions.TrimEntries).Select(part => Array.IndexOf(names, part)).Where(index => index >= 0); }",
            preferences);

        result.ShouldBe("""
            class C { object M(string preference) => preference
                .Split(',', StringSplitOptions.TrimEntries)
                .Select(part => Array.IndexOf(names, part))
                .Where(index => index >= 0); }
            """);
        Format(result, preferences).ShouldBe("""
            class C { object M(string preference) => preference
                .Split(',', StringSplitOptions.TrimEntries)
                .Select(part => Array.IndexOf(names, part))
                .Where(index => index >= 0); }
            """);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Auto_arguments_keep_a_multiline_lambda_attached(bool useDefaults)
    {
        const string source = """
            class C
            {
                void M()
                {
                    var preferences = RuleCatalog.BuiltIn.Rules
                        .Select(rule =>
                        {
                            var key = rule.Metadata.RuleKey;
                            return new InteractivePreference(
                                key,
                                local.GetValueOrDefault(key, PreferenceAssignment.Absent),
                                inherited.GetValueOrDefault(key, PreferenceAssignment.Absent),
                                inheritedSources.GetValueOrDefault(key),
                                effective.GetValueOrDefault(key),
                                effectiveSources.GetValueOrDefault(key));
                        })
                        .ToImmutableArray();
                }
            }
            """;
        var preferences = useDefaults
            ? PreferenceCatalog.Defaults
                .Select(item => (item.Key.ToName(), item.Key == RuleKey.MaxLineLength ? "160" : item.Default))
                .ToArray()
            : [("dress_arguments_layout", "auto"), ("max_line_length", "160")];

        var result = Format(source, preferences);

        result.Contains(".Select(rule =>").ShouldBe(true);
        if (!useDefaults)
            result.ShouldBe(source);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Auto_arguments_indent_from_object_initializer_member(string lineEnding)
    {
        const string source = """
            sealed class ModifierOrderRule
            {
                public RuleMetadata Metadata { get; } = new()
                {
                    Description = "Controls member modifier lists. Only modifier token order changes.",
                    Values = RuleValues.Permutation("public", "protected", "internal", "private", "file", "new", "static", "abstract", "virtual", "sealed", "override", "readonly", "unsafe", "required", "volatile", "async")
                };
            }
            """;
        const string expected = """
            sealed class ModifierOrderRule
            {
                public RuleMetadata Metadata { get; } = new()
                    {
                        Description = "Controls member modifier lists. Only modifier token order changes.",
                        Values = RuleValues.Permutation(
                            "public",
                            "protected",
                            "internal",
                            "private",
                            "file",
                            "new",
                            "static",
                            "abstract",
                            "virtual",
                            "sealed",
                            "override",
                            "readonly",
                            "unsafe",
                            "required",
                            "volatile",
                            "async"
                        )
                    };
            }
            """;
        var preferences = PreferenceCatalog.Defaults
            .Select(item => (item.Key.ToName(), item.Key == RuleKey.MaxLineLength ? "160" : item.Default))
            .ToArray();

        var input = source.ReplaceLineEndings(lineEnding);
        var result = Format(input, preferences);

        result.ShouldBe(expected.ReplaceLineEndings(lineEnding));
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData(160, false)]
    [InlineData(161, true)]
    public void Auto_arguments_measure_the_lambda_header_line(int width, bool wraps)
    {
        var parameter = new string('x', width - "Select( =>".Length);
        var source = $"Select({parameter} =>\n{{\n    return value;\n}});";
        var preferences = new[] { ("dress_arguments_layout", "auto"), ("max_line_length", "160") };

        var result = Format(source, preferences);

        result.Contains("Select(\n").ShouldBe(wraps);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Auto_arguments_ignore_overlong_lines_inside_a_lambda_body()
    {
        var source = $"Select(rule =>\n{{\n    return {new string('x', 200)};\n}});";
        var preferences = new[] { ("dress_arguments_layout", "auto"), ("max_line_length", "160") };

        var result = Format(source, preferences);

        result.ShouldBe(source);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Auto_layout_keeps_a_conditional_indented_inside_a_multiline_lambda_argument(string lineEnding)
    {
        const string source = """
            class C
            {
                void M()
                {
                    _ = 0;
                    _occurrences.Sort(static (left, right) =>
                        {
                            var first = left.FirstToken.CompareTo(right.FirstToken);
                            return first != 0
                                ? first
                                : right.LastToken.CompareTo(left.LastToken);
                        });
                }
            }
            """;
        var preferences = new[]
        {
            ("dress_arguments_layout", "auto"),
            ("dress_binary_expressions_layout", "auto"),
            ("dress_conditional_expressions_layout", "auto"),
            ("dress_nested_conditional_style", "flat"),
            ("dotnet_style_operator_placement_when_wrapping", "beginning_of_line"),
            ("dress_lambda_block_indentation", "indented"),
            ("csharp_indent_block_contents", "true"),
            ("max_line_length", "160")
        };

        var input = source.ReplaceLineEndings(lineEnding);
        var result = Format(input, preferences);

        result.ShouldBe(input);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Auto_layout_keeps_conditional_branches_indented_inside_a_binary_operand(string lineEnding)
    {
        const string source = """
            class C
            {
                string M()
                {
                    return prefix + (value is
                        A
                        or B
                            ? yes
                            : no);
                }
            }
            """;
        var preferences = new[]
        {
            ("dress_binary_expressions_layout", "auto"),
            ("dress_conditional_expressions_layout", "auto"),
            ("dress_nested_conditional_style", "decision_ladder"),
            ("dotnet_style_operator_placement_when_wrapping", "beginning_of_line"),
            ("csharp_indent_block_contents", "true"),
            ("max_line_length", "160")
        };

        var input = source.ReplaceLineEndings(lineEnding);
        var result = Format(input, preferences);

        result.ShouldBe(input);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Auto_layout_keeps_a_property_pattern_indented_as_the_right_operand_of_is(string lineEnding)
    {
        const string source = """
            class C
            {
                void M(bool multi, Occurrence occurrence)
                {
                    if (!multi
                        && occurrence.Setting is
                            { Mode: WrappingMode.Auto or WrappingMode.Compact, MaximumLineLength: not int.MaxValue })
                    {
                    }
                }
            }
            """;
        var preferences = new[]
        {
            ("dress_binary_expressions_layout", "auto"),
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_braces", "false"),
            ("max_line_length", "160")
        };

        var input = source.ReplaceLineEndings(lineEnding);
        var result = Format(input, preferences);

        result.ShouldBe(input);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Auto_layout_indents_a_property_pattern_once_from_the_is_expression(string lineEnding)
    {
        const string source = """
            class C
            {
                void M(IEnumerable<Setting> settings)
                {
                    foreach (var setting in settings)
                    {
                        _needsWidths |= setting is
                                    { Mode: WrappingMode.Auto or WrappingMode.Compact, MaximumLineLength: not int.MaxValue };
                    }
                }
            }
            """;
        const string expected = """
            class C
            {
                void M(IEnumerable<Setting> settings)
                {
                    foreach (var setting in settings)
                    {
                        _needsWidths |= setting is
                            { Mode: WrappingMode.Auto or WrappingMode.Compact, MaximumLineLength: not int.MaxValue };
                    }
                }
            }
            """;
        var preferences = new[]
        {
            ("dress_binary_expressions_layout", "auto"),
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_braces", "false"),
            ("max_line_length", "160")
        };

        var result = Format(source.ReplaceLineEndings(lineEnding), preferences);

        result.ShouldBe(expected.ReplaceLineEndings(lineEnding));
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData(160)]
    [InlineData(60)]
    public void Auto_member_access_measures_multiline_arguments_on_their_own_lines(int maximum)
    {
        const string source = """
            _ = _validated.GetOrAdd(
              configPath,
              key => new Lazy<bool>(() =>
                  {
                      EditorConfigSyntaxValidator.DecodeAndValidate(key, File.ReadAllBytes(key));
                      return true;
                  })).Value;
            """;
        var preferences = new[]
        {
            ("dress_member_access_chains_layout", "auto"), ("max_line_length", maximum.ToString())
        };

        var result = Format(source, preferences);

        result.ShouldBe(source);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Auto_member_access_wraps_only_the_overlong_chain_line()
    {
        const string source = """
            _ = _validated.GetOrAdd(
                configPath,
                key => Create(key)).VeryLongPropertyName.MoreProperties;
            """;
        const string expected = """
            _ = _validated.GetOrAdd(
                configPath,
                key => Create(key))
                .VeryLongPropertyName
                .MoreProperties;
            """;
        var preferences = new[]
        {
            ("dress_member_access_chains_layout", "auto"), ("max_line_length", "40")
        };

        var result = Format(source, preferences);

        result.ShouldBe(expected);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData(160, false)]
    [InlineData(161, true)]
    public void Auto_member_access_still_wraps_an_overlong_call_before_multiline_arguments(int width, bool wraps)
    {
        var receiver = new string('x', width - "_ = .GetOrAdd(".Length);
        var source = $"_ = {receiver}.GetOrAdd(\n    key,\n    value).Value;";
        var expected = wraps
            ? $"_ = {receiver}\n    .GetOrAdd(\n    key,\n    value).Value;"
            : source;
        var preferences = new[]
        {
            ("dress_member_access_chains_layout", "auto"), ("max_line_length", "160")
        };

        var result = Format(source, preferences);

        result.ShouldBe(expected);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Auto_member_access_keeps_multiline_lambda_calls_attached_with_default_preferences()
    {
        const string source = """
            _ = _validated.GetOrAdd(
              configPath,
              key => new Lazy<bool>(() =>
                  {
                      EditorConfigSyntaxValidator.DecodeAndValidate(key, File.ReadAllBytes(key));
                      return true;
                      })).Value;
            """;
        var preferences = PreferenceCatalog.Defaults
            .Select(item => (item.Key.ToName(), item.Key == RuleKey.MaxLineLength ? "160" : item.Default))
            .ToArray();

        var result = Format(source, preferences);

        result.Contains("_validated.GetOrAdd(").ShouldBe(true);
        result.Contains("})).Value;").ShouldBe(true);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Auto_uses_an_earlier_rewritten_members_effective_width()
    {
        const string source = "class C { int A() { return 1; } void B() { int value = 0; N(alpha, beta); } }";
        const string effectivePrefix = "class C { int A() => 1; void B() { int value = 0; ";
        var maximum = effectivePrefix.Length + "N(alpha, beta)".Length;
        var preferences = new[]
        {
            ("dress_method_body", "expression"), ("dress_arguments_layout", "auto"), ("max_line_length", maximum.ToString())
        };

        var result = Format(source, preferences);

        result.ShouldBe("class C { int A() => 1; void B() { int value = 0; N(alpha, beta); } }");
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Conditional_layout_owns_every_operator_in_an_unparenthesized_chain()
    {
        var single = Format(
            "class C { int M() => first ? second : third ? fourth : fifth; }",
            ("dress_conditional_expressions_layout", "always_single"));
        var multi = Format(
            "class C { int M() => first ? second : third ? fourth : fifth; }",
            ("dress_conditional_expressions_layout", "always_multi"));

        single.ShouldBe("class C { int M() => first ? second : third ? fourth : fifth; }");
        multi.ShouldBe("""
            class C { int M() => first
                ? second
                : third
                ? fourth
                : fifth; }
            """);
        Format(multi, ("dress_conditional_expressions_layout", "always_single")).ShouldBe(single);
        Format(multi, ("dress_conditional_expressions_layout", "always_multi")).ShouldBe(multi);
    }

    [Fact]
    public void Wrapped_operators_keep_their_operand_on_the_same_line()
    {
        var preferences = new[]
        {
            ("dress_binary_expressions_layout", "always_multi"), ("csharp_space_around_binary_operators", "before_and_after")
        };

        var result = Format("""
            class C { bool M() => first ||
                second; }
            """,
            preferences);

        result.ShouldBe("""
            class C { bool M() => first
                || second; }
            """);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Nested_conditional_line_comments_prevent_unsafe_compaction()
    {
        const string source = """
            class C { int M() => first
                ? second
                : third // keep nested condition attached
                    ? fourth
                    : fifth; }
            """;

        Format(source, ("dress_conditional_expressions_layout", "always_single")).ShouldBe(Format(source));
    }

    [Theory]
    [InlineData(
        "class C { string M(string value) => value?.Trim(); }",
        "class C { string M(string value) => value?.Trim(); }",
        """
        class C { string M(string value) => value
            ?.Trim(); }
        """)]
    [InlineData(
        "class C { string M(string value) => value?.Trim().ToString(); }",
        "class C { string M(string value) => value?.Trim().ToString(); }",
        """
        class C { string M(string value) => value
            ?.Trim()
            .ToString(); }
        """)]
    public void Conditional_access_chains_support_all_modes_without_splitting_question_dot(
        string source,
        string expectedSingle,
        string expectedMulti)
    {
        var single = Format(source, ("dress_member_access_chains_layout", "always_single"));
        var multi = Format(source, ("dress_member_access_chains_layout", "always_multi"));

        single.ShouldBe(expectedSingle);
        multi.ShouldBe(expectedMulti);
        Format(source, ("dress_member_access_chains_layout", "auto"), ("max_line_length", "500")).ShouldBe(single);
        Format(source, ("dress_member_access_chains_layout", "auto"), ("max_line_length", "1")).ShouldBe(multi);
        Format(multi, ("dress_member_access_chains_layout", "always_multi")).ShouldBe(multi);
    }

    [Fact]
    public void Syntax_wrapping_overrides_earlier_spacing_rules_on_owned_gaps()
    {
        var result = Format(
            "class C { void M() { N(alpha,beta); } void N(int a, int b) {} int alpha; int beta; }",
            ("dress_arguments_layout", "always_single"),
            ("csharp_space_after_comma", "false"),
            ("csharp_space_between_method_call_parameter_list_parentheses", "true"));

        result.ShouldBe("class C { void M() { N(alpha, beta); } void N(int a,int b) {} int alpha; int beta; }");
        Format(
            result,
            ("dress_arguments_layout", "always_single"),
            ("csharp_space_after_comma", "false"),
            ("csharp_space_between_method_call_parameter_list_parentheses", "true")).ShouldBe(result);
    }

    [Fact]
    public void Later_query_new_line_rule_overrides_syntax_wrapping()
    {
        var expanded = Format(
            "class C { object M(int[] xs) => from x in xs where x > 0 select x; }",
            ("dress_query_clauses_layout", "always_single"),
            ("csharp_new_line_between_query_expression_clauses", "true"));
        var compacted = Format(
            "class C { object M(int[] xs) => from x in xs where x > 0 select x; }",
            ("dress_query_clauses_layout", "always_multi"),
            ("csharp_new_line_between_query_expression_clauses", "false"));

        expanded.ShouldBe("""
            class C { object M(int[] xs) => from x in xs
            where x > 0
            select x; }
            """);
        compacted.ShouldBe("class C { object M(int[] xs) => from x in xs where x > 0 select x; }");
        Format(
            expanded,
            ("dress_query_clauses_layout", "always_single"),
            ("csharp_new_line_between_query_expression_clauses", "true")).ShouldBe(expanded);
        Format(
            compacted,
            ("dress_query_clauses_layout", "always_multi"),
            ("csharp_new_line_between_query_expression_clauses", "false")).ShouldBe(compacted);
    }

    [Fact]
    public void Initializer_layout_overrides_later_member_new_line_rule()
    {
        var result = Format(
            "class C { C M() => new C { X = 1, Y = 2 }; int X; int Y; }",
            ("dress_object_initializer_layout", "compact"),
            ("csharp_new_line_before_members_in_object_initializers", "true"));

        result.ShouldBe("class C { C M() => new C { X = 1, Y = 2 }; int X; int Y; }");
        Format(
            result,
            ("dress_object_initializer_layout", "compact"),
            ("csharp_new_line_before_members_in_object_initializers", "true")).ShouldBe(result);
    }

    [Theory]
    [InlineData("indented",
        """
        class C
        {
            C M() => new C
                {
                    X = 1,
                    Y = 2
                };
            int X;
            int Y;
        }
        """)]
    [InlineData("not_indented",
        """
        class C
        {
            C M() => new C
            {
                X = 1,
                Y = 2
            };
            int X;
            int Y;
        }
        """)]
    public void Earlier_initializer_indentation_sets_the_base_used_by_syntax_wrapping(
        string indentation,
        string expected)
    {
        var result = Format(
            """
            class C
            {
                C M() => new C
                {
                    X = 1, Y = 2
                };
                int X;
                int Y;
            }
            """,
            ("dress_object_initializer_indentation", indentation),
            ("dress_object_initializer_layout", "expanded"),
            ("csharp_indent_block_contents", "true"));

        result.ShouldBe(expected);
        Format(
            result,
            ("dress_object_initializer_indentation", indentation),
            ("dress_object_initializer_layout", "expanded"),
            ("csharp_indent_block_contents", "true")).ShouldBe(result);
    }

    [Fact]
    public void Directives_and_malformed_occurrences_do_not_block_safe_occurrences()
    {
        const string directives = """
            class C { void A(int a, int b) {}
            #if X
            void B(int a, int b) {}
            #endif
            }
            """;

        const string malformed = "class C { void A(int a, int b) {} void B(int a, int b {} }";

        var directiveResult = Format(directives, ("dress_parameters_layout", "always_multi"));
        var malformedResult = Format(malformed, ("dress_parameters_layout", "always_multi"));

        directiveResult.ShouldBe("""
            class C { void A(
                int a,
                int b
            ) {}
            #if X
            void B(int a, int b) {}
            #endif
            }
            """);
        malformedResult.ShouldBe("""
            class C { void A(
                int a,
                int b
            ) {} void B(int a, int b {} }
            """);
    }

    [Fact]
    public void Comments_are_preserved_and_line_comments_prevent_unsafe_compaction()
    {
        const string blockComment = "class C { void M() { N(alpha, /* beta */ beta); } void N(int a, int b) {} int alpha; int beta; }";

        const string lineComment = "class C { void M(\n    int alpha, // keep beta attached\n    int beta) {} }";

        var expanded = Format(blockComment, ("dress_arguments_layout", "always_multi"));

        expanded.ShouldBe("""
            class C { void M() { N(
                alpha,
                /* beta */ beta
            ); } void N(int a, int b) {} int alpha; int beta; }
            """);
        Format(expanded, ("dress_arguments_layout", "always_multi")).ShouldBe(expanded);
        Format(lineComment, ("dress_parameters_layout", "always_single")).ShouldBe(Format(lineComment));
    }

    [Fact]
    public void Configured_line_endings_and_tab_indentation_are_preserved()
    {
        const string sourceTemplate = """
            class C
            {
            	void M(int alpha, int beta) {}
            }
            """;
        const string expectedTemplate = """
            class C
            {
            	void M(
            		int alpha,
            		int beta
            	) {}
            }
            """;
        var source = sourceTemplate.Replace("\n", "\r\n");
        var expected = expectedTemplate.Replace("\n", "\r\n");

        var result = Format(
            source,
            ("dress_parameters_layout", "always_multi"),
            ("indent_style", "tab"),
            ("tab_width", "4"),
            ("csharp_indent_block_contents", "true"));

        result.ShouldBe(expected);
    }

    [Fact]
    public void Default_preferences_enable_every_syntax_wrapping_rule()
    {
        string[] keys =
        [
            "dress_arguments_layout",
            "dress_parameters_layout",
            "dress_object_initializer_layout",
            "dress_collection_initializer_layout",
            "dress_array_initializer_layout",
            "dress_with_initializer_layout",
            "dress_collection_expressions_layout",
            "dress_base_type_lists_layout",
            "dress_constraint_clauses_layout",
            "dress_member_access_chains_layout",
            "dress_binary_expressions_layout",
            "dress_conditional_expressions_layout",
            "dress_query_clauses_layout",
            "dress_attributes_layout"
        ];

        var defaults = PreferenceCatalog.Defaults.ToDictionary(item => item.Key, item => item.Default);

        keys.AllItemsSatisfy(key => defaults[RuleKeys.Parse(key)].ShouldBe("auto"));
        defaults[RuleKey.MaxLineLength].ShouldBe("180");
    }

    static string Format(string source, params (string Key, string Value)[] preferences) =>
        EmitterTestHarness.Format(source, preferences);
}
