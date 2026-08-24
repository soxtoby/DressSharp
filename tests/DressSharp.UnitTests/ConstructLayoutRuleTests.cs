using DressSharp.Architecture;
using DressSharp.Configuration;
using Xunit;

namespace DressSharp.UnitTests;

public class ConstructLayoutRuleTests
{
    public static TheoryData<string, string, string, string> Constructs => new()
    {
        {
            "dress_arguments_layout",
            "class C { void M() { N(alpha, beta); } void N(int a, int b) {} int alpha; int beta; }",
            "N(alpha, beta)",
            "N(\n"
        },
        {
            "dress_parameters_layout",
            "class C { void M(int alpha, string beta) {} }",
            "M(int alpha, string beta)",
            "M(\n"
        },
        {
            "dress_initializers_layout",
            "class C { object M() => new C { A = 1, B = 2 }; int A; int B; }",
            "new C {A = 1, B = 2}",
            "new C {\n"
        },
        {
            "dress_collection_expressions_layout",
            "class C { int[] M() => [alpha, beta]; int alpha; int beta; }",
            "[alpha, beta]",
            "[\n"
        },
        {
            "dress_base_type_lists_layout",
            "class C : Alpha, IBeta {} class Alpha {} interface IBeta {}",
            "class C : Alpha, IBeta",
            "class C :\n"
        },
        {
            "dress_constraint_clauses_layout",
            "class C<T, U> where T : class where U : struct {}",
            "C<T, U> where T",
            "C<T, U>\n"
        },
        {
            "dress_member_access_chains_layout",
            "class C { string M() => value.Trim().ToString(); string value = \"\"; }",
            "value.Trim().ToString()",
            "value\n"
        },
        {
            "dress_binary_expressions_layout",
            "class C { bool M() => alpha && beta; bool alpha; bool beta; }",
            "alpha && beta",
            "alpha\n"
        },
        {
            "dress_conditional_expressions_layout",
            "class C { int M() => condition ? alpha : beta; bool condition; int alpha; int beta; }",
            "condition ? alpha : beta",
            "condition\n"
        },
        {
            "dress_query_clauses_layout",
            "class C { object M(int[] xs) => from x in xs where x > 0 select x; }",
            "xs where x > 0 select x",
            "xs\n"
        },
        {
            "dress_attributes_layout",
            "[A, B] class C {} class AAttribute : System.Attribute {} class BAttribute : System.Attribute {}",
            "[A, B]",
            "[\n"
        }
    };

    [Theory]
    [MemberData(nameof(Constructs))]
    public void Each_construct_supports_all_layout_modes_and_is_idempotent(
        string key,
        string source,
        string singleMarker,
        string multiMarker)
    {
        var single = Format(source, (key, "always_single"));
        var multi = Format(source, (key, "always_multi"));
        var automaticSingle = Format(source, (key, "auto"), ("max_line_length", "500"));
        var automaticMulti = Format(source, (key, "auto"), ("max_line_length", "1"));

        Assert.Contains(singleMarker, single);
        Assert.Contains(multiMarker, multi);
        Assert.Equal(single, automaticSingle);
        Assert.Equal(multi, automaticMulti);
        Assert.NotEqual(single, multi);
        Assert.Equal(single, Format(multi, (key, "always_single")));
        Assert.Equal(multi, Format(multi, (key, "always_multi")));
    }

    [Fact]
    public void Auto_wraps_only_after_the_visual_width_exceeds_the_maximum()
    {
        const string source = "class C\n{\n    void M(int alpha, int beta) {}\n}";

        Assert.Equal(
            Format(source, ("dress_parameters_layout", "always_single")),
            Format(source, ("dress_parameters_layout", "auto"), ("max_line_length", "31")));
        Assert.Equal(
            Format(source, ("dress_parameters_layout", "always_multi")),
            Format(source, ("dress_parameters_layout", "auto"), ("max_line_length", "30")));
    }

    [Fact]
    public void Auto_with_max_line_length_off_chooses_single_line()
    {
        const string source = "class C { void M(int alpha, int beta) {} }";

        Assert.Equal(
            Format(source, ("dress_parameters_layout", "always_single")),
            Format(source, ("dress_parameters_layout", "auto"), ("max_line_length", "off")));
    }

    [Fact]
    public void Constraint_auto_measures_the_declaration_header_not_its_body()
    {
        const string source =
            "class C<T> where T : class { string Value => \"this body is deliberately much wider than the header\"; }";

        Assert.Equal(
            Format(source, ("dress_constraint_clauses_layout", "always_single")),
            Format(
                source,
                ("dress_constraint_clauses_layout", "auto"),
                ("max_line_length", "40")));
    }

    [Fact]
    public void Query_continuations_support_all_layout_modes_recursively()
    {
        const string source =
            "class C { object M(int[] xs) => from x in xs group x by x into grouped where grouped.Any() select grouped; }";
        var single = Format(source, ("dress_query_clauses_layout", "always_single"));
        var multi = Format(source, ("dress_query_clauses_layout", "always_multi"));

        Assert.Contains("group x by x into grouped where grouped.Any() select grouped", single);
        Assert.Contains("group x by x\n", multi);
        Assert.Contains("into grouped\n", multi);
        Assert.Contains("where grouped.Any()\n", multi);
        Assert.Equal(
            single,
            Format(source, ("dress_query_clauses_layout", "auto"), ("max_line_length", "500")));
        Assert.Equal(
            multi,
            Format(source, ("dress_query_clauses_layout", "auto"), ("max_line_length", "1")));
        Assert.Equal(multi, Format(multi, ("dress_query_clauses_layout", "always_multi")));
    }

    [Fact]
    public void Query_continuation_line_comments_prevent_unsafe_compaction()
    {
        const string source =
            "class C { object M(int[] xs) => from x in xs group x by x\n"
            + "into grouped // keep continuation attached\n"
            + "where grouped.Any()\n"
            + "select grouped; }";

        Assert.Equal(
            Format(source),
            Format(source, ("dress_query_clauses_layout", "always_single")));
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

        Assert.Equal(
            Format(malformed),
            Format(malformed, ("dress_query_clauses_layout", "always_multi")));
        Assert.Equal(
            Format(directives),
            Format(directives, ("dress_query_clauses_layout", "always_multi")));
    }

    [Fact]
    public void Auto_counts_preserved_block_comment_text()
    {
        const string plain = "class C { void M() { N(alpha, beta); } void N(int a, int b) {} int alpha; int beta; }";
        const string commented = "class C { void M() { N(alpha, /* this comment makes the invocation much too wide */ beta); } void N(int a, int b) {} int alpha; int beta; }";
        var preferences = new[]
        {
            ("dress_arguments_layout", "auto"),
            ("max_line_length", "60")
        };

        Assert.Contains("N(alpha, beta)", Format(plain, preferences));
        Assert.Contains("N(\n", Format(commented, preferences));
    }

    [Fact]
    public void Auto_measures_spacing_rules_that_run_before_construct_layout()
    {
        const string source = "class C { bool M() => alpha&&beta; bool alpha; bool beta; }";

        var compact = Format(
            source,
            ("dress_binary_expressions_layout", "auto"),
            ("csharp_space_around_binary_operators", "none"),
            ("max_line_length", "34"));
        var spaced = Format(
            source,
            ("dress_binary_expressions_layout", "auto"),
            ("csharp_space_around_binary_operators", "before_and_after"),
            ("max_line_length", "34"));

        Assert.Contains("alpha &&beta", compact);
        Assert.Contains("alpha\n", spaced);
    }

    [Fact]
    public void Absent_and_unset_preferences_leave_constructs_alone()
    {
        const string source = "class C { void M(int alpha, int beta) {} }";

        Assert.Equal(source, Format(source));
        Assert.Equal(source, Format(source, ("dress_parameters_layout", "unset")));
    }

    [Fact]
    public void Nested_rules_compose_in_one_idempotent_batch()
    {
        const string source = "class C { void M() { N(alpha + beta, gamma); } void N(int a, int b) {} int alpha; int beta; int gamma; }";
        var preferences = new[]
        {
            ("dress_arguments_layout", "always_multi"),
            ("dress_binary_expressions_layout", "always_multi")
        };

        var result = Format(source, preferences);

        Assert.Contains("N(\n", result);
        Assert.Contains("alpha\n", result);
        Assert.Equal(result, Format(result, preferences));
    }

    [Fact]
    public void Outer_initializer_auto_layout_uses_original_position_after_inner_argument_rewrite()
    {
        const string source = "class C\n{\n    object[] M() => new[]\n    {\n        N(firstArgument, secondArgument)\n    };\n}";
        var preferences = new[]
        {
            ("dress_arguments_layout", "auto"),
            ("dress_initializers_layout", "auto"),
            ("max_line_length", "20")
        };

        var result = Format(source, preferences);

        Assert.Contains("N(\n", result);
        Assert.Equal(result, Format(result, preferences));
    }

    [Theory]
    [InlineData(
        "dress_arguments_layout",
        "class C { object M() => Outer(Inner(firstArgument, secondArgument), thirdArgument); }")]
    [InlineData(
        "dress_collection_expressions_layout",
        "class C { object[] M() => [N(firstArgument, secondArgument), thirdArgument]; }")]
    [InlineData(
        "dress_member_access_chains_layout",
        "class C { object M() => N(firstArgument, secondArgument).First.Second; }")]
    [InlineData(
        "dress_binary_expressions_layout",
        "class C { int M() => N(firstArgument, secondArgument) + thirdArgument; }")]
    [InlineData(
        "dress_conditional_expressions_layout",
        "class C { int M() => N(firstArgument, secondArgument) ? firstArgument : secondArgument; }")]
    [InlineData(
        "dress_query_clauses_layout",
        "class C { object M(int[] xs) => from x in xs where N(firstArgument, secondArgument) select x; }")]
    public void Outer_auto_layout_kinds_keep_stable_source_anchors_after_inner_rewrite(
        string outerKey,
        string source)
    {
        var preferences = outerKey == "dress_arguments_layout"
            ? new[]
            {
                ("dress_arguments_layout", "auto"),
                ("max_line_length", "20")
            }
            :
            [
                ("dress_arguments_layout", "auto"),
                (outerKey, "auto"),
                ("max_line_length", "20")
            ];

        var result = Format(source, preferences);

        Assert.Contains('\n', result);
        Assert.Equal(result, Format(result, preferences));
    }

    [Fact]
    public void Layout_plan_uses_member_rewrite_tokens_without_rebuilding_the_tree()
    {
        const string source = "class C { object M() { return N(firstArgument, secondArgument); } }";
        var preferences = new[]
        {
            ("dress_method_body", "expression"),
            ("dress_arguments_layout", "auto"),
            ("max_line_length", "20")
        };

        var result = Format(source, preferences);

        Assert.Contains("=>N(\n", result);
        Assert.Equal(result, Format(result, preferences));
    }

    [Fact]
    public void Member_rewrite_inside_constrained_type_does_not_inherit_original_layout_ancestor()
    {
        const string source =
            "class PaddingWithLongEnoughNameToMoveTheFollowingDeclarationFarAway { } "
            + "class C<T> where T : class { object M() { return N(firstArgument, secondArgument); } }";
        var preferences = new[]
        {
            ("dress_method_body", "expression"),
            ("dress_constraint_clauses_layout", "always_single"),
            ("dress_arguments_layout", "always_multi")
        };

        var result = Format(source, preferences);

        Assert.Contains("=>N(\n", result);
        Assert.Equal(result, Format(result, preferences));
    }

    [Fact]
    public void Auto_in_rewritten_member_uses_the_original_insertion_column_for_nested_constructs()
    {
        const string source =
            "class AClassNameLongEnoughToMatter { object M() { return N(first, second); } }";
        var preferences = new[]
        {
            ("dress_method_body", "expression"),
            ("dress_arguments_layout", "auto"),
            ("max_line_length", "50")
        };

        var result = Format(source, preferences);

        Assert.Contains("N(\n", result);
        Assert.Equal(result, Format(result, preferences));
    }

    [Fact]
    public void Auto_keeps_distinct_anchors_for_two_rewritten_members()
    {
        const string source =
            "class C { object A() { return N(alpha, beta); } object B() { return N(alpha, beta); } }";
        var preferences = new[]
        {
            ("dress_method_body", "expression"),
            ("dress_arguments_layout", "auto"),
            ("max_line_length", "55")
        };

        var result = Format(source, preferences);

        Assert.Contains("A() =>N(alpha, beta)", result);
        Assert.Contains("B() =>N(\n", result);
        Assert.Equal(result, Format(result, preferences));
    }

    [Fact]
    public void Auto_uses_effective_prefix_spacing_at_the_exact_threshold()
    {
        const string source = "class C { void M() { if(true) N(alpha, beta); } }";
        var maximum = source.IndexOf("N(", StringComparison.Ordinal)
            + "N(alpha, beta)".Length;

        var compact = Format(
            source,
            ("dress_arguments_layout", "auto"),
            ("csharp_space_after_keywords_in_control_flow_statements", "false"),
            ("max_line_length", maximum.ToString()));
        var spaced = Format(
            source,
            ("dress_arguments_layout", "auto"),
            ("csharp_space_after_keywords_in_control_flow_statements", "true"),
            ("max_line_length", maximum.ToString()));

        Assert.Contains("if(true) N(alpha, beta)", compact);
        Assert.Contains("if (true) N(\n", spaced);
        Assert.Equal(spaced, Format(
            spaced,
            ("dress_arguments_layout", "auto"),
            ("csharp_space_after_keywords_in_control_flow_statements", "true"),
            ("max_line_length", maximum.ToString())));
    }

    [Fact]
    public void Auto_uses_effective_prefix_spacing_inside_a_rewritten_member()
    {
        const string source = "class C { int M() { return prefix+N(alpha, beta); } }";
        var maximum = "class C { int M() =>prefix+".Length + "N(alpha, beta)".Length;
        var common = new[]
        {
            ("dress_method_body", "expression"),
            ("dress_arguments_layout", "auto"),
            ("max_line_length", maximum.ToString())
        };

        var compact = Format(source, [.. common, ("csharp_space_around_binary_operators", "none")]);
        var spaced = Format(source, [.. common, ("csharp_space_around_binary_operators", "before_and_after")]);

        Assert.Contains("prefix+N(alpha, beta)", compact);
        Assert.Contains("prefix + N(\n", spaced);
        Assert.Equal(spaced, Format(
            spaced,
            [.. common, ("csharp_space_around_binary_operators", "before_and_after")]));
    }

    [Fact]
    public void Auto_uses_an_earlier_rewritten_members_effective_width()
    {
        const string source =
            "class C { int A() { return 1; } void B() { int value = 0; N(alpha, beta); } }";
        const string effectivePrefix =
            "class C { int A() =>1; void B() { int value = 0; ";
        var maximum = effectivePrefix.Length + "N(alpha, beta)".Length;
        var preferences = new[]
        {
            ("dress_method_body", "expression"),
            ("dress_arguments_layout", "auto"),
            ("max_line_length", maximum.ToString())
        };

        var result = Format(source, preferences);

        Assert.Contains(effectivePrefix + "N(alpha, beta)", result);
        Assert.Equal(result, Format(result, preferences));
    }

    [Fact]
    public void Conditional_layout_owns_every_operator_in_an_unparenthesized_chain()
    {
        const string source =
            "class C { int M() => first ? second : third ? fourth : fifth; }";
        var single = Format(source, ("dress_conditional_expressions_layout", "always_single"));
        var multi = Format(source, ("dress_conditional_expressions_layout", "always_multi"));

        Assert.Contains("first ? second : third ? fourth : fifth", single);
        Assert.Contains("? second\n", multi);
        Assert.Contains(": third\n", multi);
        Assert.Contains("? fourth\n", multi);
        Assert.Equal(single, Format(multi, ("dress_conditional_expressions_layout", "always_single")));
        Assert.Equal(multi, Format(multi, ("dress_conditional_expressions_layout", "always_multi")));
    }

    [Fact]
    public void Nested_conditional_line_comments_prevent_unsafe_compaction()
    {
        const string source =
            "class C { int M() => first\n"
            + "    ? second\n"
            + "    : third // keep nested condition attached\n"
            + "        ? fourth\n"
            + "        : fifth; }";

        Assert.Equal(
            Format(source),
            Format(source, ("dress_conditional_expressions_layout", "always_single")));
    }

    [Theory]
    [InlineData("value?.Trim()")]
    [InlineData("value?.Trim().ToString()")]
    public void Conditional_access_chains_support_all_modes_without_splitting_question_dot(string expression)
    {
        var source = $"class C {{ string M(string value) => {expression}; }}";
        var single = Format(source, ("dress_member_access_chains_layout", "always_single"));
        var multi = Format(source, ("dress_member_access_chains_layout", "always_multi"));

        Assert.Contains(expression, single);
        Assert.Contains("value\n    ?.Trim()", multi);
        Assert.DoesNotContain("?\n", multi);
        Assert.Equal(
            single,
            Format(source, ("dress_member_access_chains_layout", "auto"), ("max_line_length", "500")));
        Assert.Equal(
            multi,
            Format(source, ("dress_member_access_chains_layout", "auto"), ("max_line_length", "1")));
        Assert.Equal(multi, Format(multi, ("dress_member_access_chains_layout", "always_multi")));
    }

    [Fact]
    public void Construct_layout_overrides_earlier_spacing_rules_on_owned_gaps()
    {
        const string source = "class C { void M() { N(alpha,beta); } void N(int a, int b) {} int alpha; int beta; }";

        var result = Format(
            source,
            ("dress_arguments_layout", "always_single"),
            ("csharp_space_after_comma", "false"),
            ("csharp_space_between_method_call_parameter_list_parentheses", "true"));

        Assert.Contains("N(alpha, beta)", result);
        Assert.Equal(result, Format(
            result,
            ("dress_arguments_layout", "always_single"),
            ("csharp_space_after_comma", "false"),
            ("csharp_space_between_method_call_parameter_list_parentheses", "true")));
    }

    [Fact]
    public void Later_query_new_line_rule_overrides_construct_layout()
    {
        const string source = "class C { object M(int[] xs) => from x in xs where x > 0 select x; }";

        var expanded = Format(
            source,
            ("dress_query_clauses_layout", "always_single"),
            ("csharp_new_line_between_query_expression_clauses", "true"));
        var compacted = Format(
            source,
            ("dress_query_clauses_layout", "always_multi"),
            ("csharp_new_line_between_query_expression_clauses", "false"));

        Assert.Contains("xs\n", expanded);
        Assert.Contains("where x > 0\n", expanded);
        Assert.Contains("xs where x > 0 select x", compacted);
        Assert.Equal(expanded, Format(
            expanded,
            ("dress_query_clauses_layout", "always_single"),
            ("csharp_new_line_between_query_expression_clauses", "true")));
        Assert.Equal(compacted, Format(
            compacted,
            ("dress_query_clauses_layout", "always_multi"),
            ("csharp_new_line_between_query_expression_clauses", "false")));
    }

    [Fact]
    public void Later_initializer_member_new_line_rule_overrides_construct_layout()
    {
        const string source = "class C { C M() => new C { X = 1, Y = 2 }; int X; int Y; }";

        var result = Format(
            source,
            ("dress_initializers_layout", "always_single"),
            ("csharp_new_line_before_members_in_object_initializers", "true"));

        Assert.Contains("new C {X = 1,\n", result);
        Assert.Equal(result, Format(
            result,
            ("dress_initializers_layout", "always_single"),
            ("csharp_new_line_before_members_in_object_initializers", "true")));
    }

    [Theory]
    [InlineData("indented", "\n        {", "\n        };")]
    [InlineData("not_indented", "\n    {", "\n    };")]
    public void Earlier_initializer_indentation_sets_the_base_used_by_construct_layout(
        string indentation,
        string opening,
        string closing)
    {
        const string source = "class C\n{\n    C M() => new C\n    {\n        X = 1, Y = 2\n    };\n    int X;\n    int Y;\n}";

        var result = Format(
            source,
            ("dress_object_initializer_indentation", indentation),
            ("dress_initializers_layout", "always_multi"),
            ("csharp_indent_block_contents", "true"));

        Assert.Contains(opening, result);
        Assert.Contains(closing, result);
        Assert.Equal(result, Format(
            result,
            ("dress_object_initializer_indentation", indentation),
            ("dress_initializers_layout", "always_multi"),
            ("csharp_indent_block_contents", "true")));
    }

    [Fact]
    public void Directives_and_malformed_occurrences_do_not_block_safe_occurrences()
    {
        const string directives = "class C { void A(int a, int b) {}\n#if X\nvoid B(int a, int b) {}\n#endif\n}";
        const string malformed = "class C { void A(int a, int b) {} void B(int a, int b {} }";

        var directiveResult = Format(directives, ("dress_parameters_layout", "always_multi"));
        var malformedResult = Format(malformed, ("dress_parameters_layout", "always_multi"));

        Assert.Contains("void A(\n", directiveResult);
        Assert.Contains("void B(int a, int b)", directiveResult);
        Assert.Contains("void A(\n", malformedResult);
        Assert.Contains("void B(int a, int b", malformedResult);
    }

    [Fact]
    public void Comments_are_preserved_and_line_comments_prevent_unsafe_compaction()
    {
        const string blockComment = "class C { void M() { N(alpha, /* beta */ beta); } void N(int a, int b) {} int alpha; int beta; }";
        const string lineComment = "class C { void M(\n    int alpha, // keep beta attached\n    int beta) {} }";

        var expanded = Format(blockComment, ("dress_arguments_layout", "always_multi"));

        Assert.Contains("/* beta */", expanded);
        Assert.Equal(expanded, Format(expanded, ("dress_arguments_layout", "always_multi")));
        Assert.Equal(
            Format(lineComment),
            Format(lineComment, ("dress_parameters_layout", "always_single")));
    }

    [Fact]
    public void Configured_line_endings_and_tab_indentation_are_preserved()
    {
        const string source = "class C\r\n{\r\n\tvoid M(int alpha, int beta) {}\r\n}";

        var result = Format(
            source,
            ("dress_parameters_layout", "always_multi"),
            ("indent_style", "tab"),
            ("tab_width", "4"),
            ("csharp_indent_block_contents", "true"));

        Assert.Contains("M(\r\n\t\tint alpha,\r\n\t\tint beta\r\n\t)", result);
        Assert.DoesNotContain("\n", result.Replace("\r\n", ""));
    }

    [Fact]
    public void Familiar_defaults_enable_every_construct_preference()
    {
        string[] keys =
        [
            "dress_arguments_layout",
            "dress_parameters_layout",
            "dress_initializers_layout",
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

        Assert.All(keys, key => Assert.Equal("auto", defaults[RuleKeys.Parse(key)]));
        Assert.Equal("180", defaults[RuleKey.MaxLineLength]);
    }

    static string Format(string source, params (string Key, string Value)[] preferences) =>
        EmitterTestHarness.Format(source, preferences);
}
