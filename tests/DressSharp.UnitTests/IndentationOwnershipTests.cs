using EasyAssertions;
using DressSharp.Architecture;
using DressSharp.Configuration;
using Xunit;
using static DressSharp.UnitTests.EmitterTestHarness;

namespace DressSharp.UnitTests;

public class IndentationOwnershipTests
{
    [Theory]
    [InlineData("\n", "space")]
    [InlineData("\r\n", "space")]
    [InlineData("\n", "tab")]
    public void Conditional_branches_follow_deeper_precedence_indentation(string lineEnding, string indentStyle)
    {
        var expected = """
            var result = first
                || second
                    && third
                        ? yes
                        : no;
            """.ReplaceLineEndings(lineEnding);
        if (indentStyle == "tab")
            expected = expected.Replace("    ", "\t", StringComparison.Ordinal);
        var source = "var result = first || second && third ? yes : no;";
        var preferences = new[]
            {
                ("dress_binary_expressions_layout", "always_multi"),
                ("dress_binary_expression_indentation", "precedence"),
                ("dress_conditional_expressions_layout", "always_multi"),
                ("csharp_indent_block_contents", "true"),
                ("indent_style", indentStyle),
                ("indent_size", "4"),
                ("tab_width", "4")
            };
        // Supply the line ending without pre-wrapping the condition.
        source += lineEnding;
        expected += lineEnding;
        var result = Format(source, preferences);
        result.ShouldBe(expected);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Conditional_branches_follow_the_deepest_formatted_condition_line(string lineEnding)
    {
        var source = """
            var maximum = configuration.Preferences.TryGetValue(RuleKey.MaxLineLength, out var configuredMaximum)
                && int.TryParse(configuredMaximum, out var parsedMaximum)
                    ? parsedMaximum
                    : int.MaxValue;
            """.ReplaceLineEndings(lineEnding);
        var preferences = PreferenceCatalog.Defaults.Select(item => (item.Key.ToName(), item.Default)).ToArray();
        var result = Format(source, preferences);
        result.ShouldBe(source);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("\n", "??=")]
    [InlineData("\r\n", "??=")]
    [InlineData("\n", "=")]
    [InlineData("\r\n", "=")]
    public void Conditional_in_an_arrow_body_assignment_follows_the_assignment_indent(string lineEnding, string assignmentOperator)
    {
        var source = """
            class C
            {
                MalformedRegionIndex MalformedRegions =>
                    field ??= _knownWellFormed
                        ? MalformedRegionIndex.Empty
                        : new(ScanMalformedRegions(_root));
            }
            """.Replace("??=", assignmentOperator, StringComparison.Ordinal).ReplaceLineEndings(lineEnding);
        var preferences = PreferenceCatalog.Defaults.Select(item => (item.Key.ToName(), item.Default)).ToArray();
        var result = Format(source, preferences);
        result.ShouldBe(source);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Wrapped_constructor_arguments_follow_the_collection_element(string lineEnding)
    {
        var source = """
            class C
            {
                void M()
                {
                    SingleLinePreservationRule[] preservationRules =
                    [
                        new SingleLinePreservationRule(RuleKey.CSharpPreserveSingleLineBlocks, "Blocks", "Preserve single line", SingleLinePreservationKind.Blocks),
                        new SingleLinePreservationRule(RuleKey.CSharpPreserveSingleLineStatements, "Statements", "Preserve single line", SingleLinePreservationKind.Statements)
                    ];
                }
            }
            """.ReplaceLineEndings(lineEnding);
        var expected = source.Replace(
            "new SingleLinePreservationRule(RuleKey.CSharpPreserveSingleLineStatements, \"Statements\", \"Preserve single line\", SingleLinePreservationKind.Statements)",
            "new SingleLinePreservationRule("
            + lineEnding
            + "                RuleKey.CSharpPreserveSingleLineStatements,"
            + lineEnding
            + "                \"Statements\","
            + lineEnding
            + "                \"Preserve single line\","
            + lineEnding
            + "                SingleLinePreservationKind.Statements"
            + lineEnding
            + "            )",
            StringComparison.Ordinal);
        (string Key, string Value)[] preferences =
            [
                ("indent_style", "space"),
                ("indent_size", "4"),
                ("csharp_indent_block_contents", "true"),
                ("dress_arguments_layout", "auto"),
                ("dress_collection_expressions_layout", "auto"),
                ("max_line_length", "160")
            ];

        var result = Format(source, preferences);
        result.ShouldBe(expected, result);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [
        InlineData("flat", "bool Unsafe(SyntaxNode node) => node.ContainsDirectives\n    || ReferenceEquals(node.SyntaxTree, _root.SyntaxTree)\n    && _safetyContext?.IsUnsafe(node) == true;")
    ]
    [
        InlineData("precedence", "bool Unsafe(SyntaxNode node) => node.ContainsDirectives\n    || ReferenceEquals(node.SyntaxTree, _root.SyntaxTree)\n        && _safetyContext?.IsUnsafe(node) == true;")
    ]
    public void Binary_indentation_can_follow_operator_precedence(string indentation, string expected)
    {
        const string source = "bool Unsafe(SyntaxNode node) => node.ContainsDirectives\n    || ReferenceEquals(node.SyntaxTree, _root.SyntaxTree)\n    && _safetyContext?.IsUnsafe(node) == true;";
        var preferences = new[]
            {
                ("dress_binary_expressions_layout", "auto"),
                ("dress_binary_expression_indentation", indentation),
                ("dotnet_style_operator_placement_when_wrapping", "beginning_of_line"),
                ("max_line_length", "160")
            };
        var result = Format(source, preferences);
        result.ShouldBe(expected);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Precedence_indentation_does_not_indent_operators_at_the_same_precedence()
    {
        const string source = "var result = first\n    + second\n    - third;";
        var preferences = new[]
            {
                ("dress_binary_expressions_layout", "auto"),
                ("dress_binary_expression_indentation", "precedence")
            };
        var result = Format(source, preferences);
        result.ShouldBe(source);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Precedence_indentation_adds_a_level_for_each_higher_precedence_group()
    {
        const string source = "var result = first || second && third == fourth + fifth;";
        const string expected = "var result = first\n    || second\n        && third\n            == fourth\n                + fifth;";
        var preferences = new[]
            {
                ("dress_binary_expressions_layout", "always_multi"),
                ("dress_binary_expression_indentation", "precedence")
            };
        var result = Format(source, preferences);
        result.ShouldBe(expected);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Binary_continuations_follow_a_multiline_lambda_in_a_collection_expression(string lineEnding)
    {
        var expected = """
            abstract class B
            {
                internal abstract ImmutableArray<SyntaxKind> TriggerKinds { get; }
            }

            class C : B
            {
                internal override ImmutableArray<SyntaxKind> TriggerKinds { get; } =
                    [
                        .. Enum.GetValues<SyntaxKind>()
                            .Where(kind =>
                                SyntaxFacts.GetBinaryExpression(kind) != SyntaxKind.None
                                || SyntaxFacts.GetAssignmentExpression(kind) != SyntaxKind.None
                            )
                    ];
            }
            """.ReplaceLineEndings(lineEnding);
        var source = expected.Replace(
            lineEnding + "                    ||",
            lineEnding + "    ||",
            StringComparison.Ordinal);
        var preferences = PreferenceCatalog.Defaults
            .Select(item => (item.Key.ToName(), item.Key switch
                {
                    RuleKey.DressBinaryExpressionIndentation => "precedence",
                    RuleKey.DressArgumentsClosingDelimiterPosition => "own_line",
                    _ => item.Default
                }))
            .ToArray();

        var result = Format(source, preferences);

        result.ShouldBe(expected);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Rewritten_loop_body_retains_argument_continuation_anchors(string lineEnding)
    {
        var source = """
            void Delimited<T>(SeparatedSyntaxList<T> items, SyntaxToken close, bool spacesInside = false) where T : SyntaxNode
            {
                for (var index = 0; index < items.Count; index++)
                    AddBoundary(
                        items[index].GetFirstToken(),
                        index == 0
                            ? spacesInside ? GapStyle.DelimitedSpacedFirst : GapStyle.DelimitedFirst
                            : GapStyle.DelimitedLater);
            }
            """.ReplaceLineEndings(lineEnding);
        var expected = """
            void Delimited<T>(SeparatedSyntaxList<T> items, SyntaxToken close, bool spacesInside = false) where T : SyntaxNode
            {
                for (var index = 0; index < items.Count; index++)
                {
                    AddBoundary(
                        items[index].GetFirstToken(),
                        index == 0
                            ? spacesInside
                                ? GapStyle.DelimitedSpacedFirst
                                : GapStyle.DelimitedFirst
                            : GapStyle.DelimitedLater
                    );
                }
            }
            """.ReplaceLineEndings(lineEnding);
        var preferences = PreferenceCatalog.Defaults
            .Select(item => (item.Key.ToName(), item.Key switch
                {
                    RuleKey.DressNestedConditionalStyle => "decision_ladder",
                    RuleKey.DressArgumentsClosingDelimiterPosition => "own_line",
                    _ => item.Default
                }))
            .ToArray();
        var result = Format(source, preferences);
        result.ShouldBe(expected);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("\n", false)]
    [InlineData("\r\n", false)]
    [InlineData("\n", true)]
    [InlineData("\r\n", true)]
    public void Parenthesized_conditions_indent_each_group_from_where_it_opens(string lineEnding, bool reindent)
    {
        var source = """
            if (BelongsToOccurrence(token, occurrence)
                && (token.IsKind(SyntaxKind.QuestionToken)
                    && token.Parent is ConditionalAccessExpressionSyntax
                    || (token.IsKind(SyntaxKind.DotToken)
                        && !token.GetPreviousToken().IsKind(SyntaxKind.QuestionToken))
                    || token.IsKind(SyntaxKind.MinusGreaterThanToken)))
            {
                AddBoundary(index, GapStyle.CompactItem);
            }
            """.ReplaceLineEndings(lineEnding);
        var expected = source;
        if (reindent)
        {
            var header = "void M()" + lineEnding + "{" + lineEnding;
            var footer = lineEnding + "}";
            expected = header + string.Join(lineEnding, source.Split(lineEnding).Select(line => "    " + line)) + footer;
            source = header + source + footer;
        }
        var preferences = new[]
            {
                ("csharp_indent_block_contents", "true"),
                ("csharp_indent_braces", "false"),
                ("dress_binary_expressions_layout", "auto"),
                ("dotnet_style_operator_placement_when_wrapping", "beginning_of_line"),
                ("max_line_length", "160")
            };
        var result = Format(source, preferences);
        result.ShouldBe(expected);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("\n", "unset", "100")]
    [InlineData("\r\n", "unset", "100")]
    [InlineData("\n", "auto", "100")]
    [InlineData("\r\n", "auto", "100")]
    [InlineData("\n", "auto", "160")]
    [InlineData("\r\n", "auto", "160")]
    public void Conditional_branches_follow_their_multiline_argument(string lineEnding, string argumentsLayout, string maximumLineLength)
    {
        var source = """
            AddBoundary(
                right,
                index == 0 || !right.GetPreviousToken().IsKind(SyntaxKind.CommaToken)
                    ? GapStyle.SeparatedFirst
                    : GapStyle.SeparatedLater);
            """.ReplaceLineEndings(lineEnding);
        var preferences = new[]
            {
                ("csharp_indent_block_contents", "true"),
                ("dress_arguments_layout", argumentsLayout),
                ("dress_conditional_expressions_layout", "auto"),
                ("dress_nested_conditional_style", "decision_ladder"),
                ("max_line_length", maximumLineLength)
            };
        var result = Format(source, preferences);
        result.ShouldBe(source);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Conditional_invocation_arguments_follow_the_conditional_branch(string lineEnding)
    {
        var source = """
            class C
            {
                public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node) =>
                    kind == MemberBodyKind.Method
                        ? RewriteCallable(node, node.Body, node.ExpressionBody, node.SemicolonToken, node.ReturnType is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.VoidKeyword })
                        : base.VisitMethodDeclaration(node);
            }
            """.ReplaceLineEndings(lineEnding);
        var expected = """
            class C
            {
                public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node) =>
                    kind == MemberBodyKind.Method
                        ? RewriteCallable(
                            node,
                            node.Body,
                            node.ExpressionBody,
                            node.SemicolonToken,
                            node.ReturnType is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.VoidKeyword }
                        )
                        : base.VisitMethodDeclaration(node);
            }
            """.ReplaceLineEndings(lineEnding);
        var preferences = PreferenceCatalog.Defaults
            .Select(item => (item.Key.ToName(), item.Key switch
                {
                    RuleKey.MaxLineLength => "160",
                    RuleKey.DressArgumentsClosingDelimiterPosition => "own_line",
                    _ => item.Default
                }))
            .ToArray();
        var result = Format(source, preferences);
        result.ShouldBe(expected);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Nested_invocation_arguments_follow_their_expression_body(string lineEnding)
    {
        var source = """
            class C
            {
                static AccessorListSyntax Getter(ArrowExpressionClauseSyntax arrow, SyntaxToken semicolon) =>
                    SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration, GeneratedSyntax.Mark(SyntaxFactory.Block(Return(arrow.Expression))))));
            }
            """.ReplaceLineEndings(lineEnding);
        var expected = """
            class C
            {
                static AccessorListSyntax Getter(ArrowExpressionClauseSyntax arrow, SyntaxToken semicolon) =>
                    SyntaxFactory.AccessorList(
                        SyntaxFactory.SingletonList(
                            SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration, GeneratedSyntax.Mark(SyntaxFactory.Block(Return(arrow.Expression))))
                        )
                    );
            }
            """.ReplaceLineEndings(lineEnding);
        var preferences = PreferenceCatalog.Defaults
            .Select(item => (item.Key.ToName(), item.Key switch
                {
                    RuleKey.MaxLineLength => "160",
                    RuleKey.DressArgumentsClosingDelimiterPosition => "own_line",
                    _ => item.Default
                }))
            .ToArray();
        var result = Format(source, preferences);
        result.ShouldBe(expected);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Binary_continuations_follow_their_multiline_argument()
    {
        const string source = """
            M(
                first,
                second
                    && third);
            """;
        var preferences = new[]
            {
                ("csharp_indent_block_contents", "true"),
                ("dress_binary_expressions_layout", "always_multi")
            };
        var result = Format(source, preferences);
        result.ShouldBe(source);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Property_patterns_follow_their_binary_expression_continuation(string lineEnding)
    {
        var source = """
            class C
            {
                void M()
                {
                    bool CanConvert(LambdaExpressionSyntax node) => expression
                        && node.Body is BlockSyntax
                        {
                            Statements: [ReturnStatementSyntax { Expression: not null } or ExpressionStatementSyntax]
                        };
                }
            }
            """.ReplaceLineEndings(lineEnding);
        var preferences = PreferenceCatalog.Defaults
            .Select(item => (item.Key.ToName(), item.Default))
            .ToArray();
        var result = Format(source, preferences);
        result.ShouldBe(source);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Nested_block_braces_and_wrapping_share_the_same_anchor()
    {
        const string source = """
            if (ready)
                {
                        {
                            result = firstCondition
                                && secondCondition;
                        }
                }
            """;
        var preferences = new[]
            {
                ("csharp_indent_block_contents", "true"),
                ("csharp_indent_braces", "true"),
                ("dress_binary_expressions_layout", "always_multi")
            };
        var result = Format(source.Replace("    ", ""), preferences);
        result.ShouldBe(source);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Disabling_block_indentation_also_changes_wrapping_anchors()
    {
        const string source = """
            class C
            {
                void M()
                {
                if (firstCondition
                    && secondCondition)
                {
                Work();
                }
                }
            }
            """;
        var preferences = new[]
            {
                ("csharp_indent_block_contents", "false"),
                ("csharp_indent_braces", "false"),
                ("dress_binary_expressions_layout", "always_multi")
            };
        var result = Format(source.Replace("    ", ""), preferences);
        result.ShouldBe(source);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Case_preferences_apply_to_bodies_and_wrapped_expressions(bool indentLabels, bool indentContents)
    {
        var label = indentLabels ? "\t" : "";
        var statement = label + (indentContents ? "\t" : "");
        var source = $"switch (value) {{\n{label}case 0:\n{statement}if (firstCondition\n{statement}\t&& secondCondition)\n{statement}{{\n{statement}\tWork();\n{statement}}}\n}}";
        var preferences = new[]
            {
                ("indent_style", "tab"),
                ("csharp_indent_block_contents", "true"),
                ("csharp_indent_braces", "false"),
                ("csharp_indent_switch_labels", indentLabels ? "true" : "false"),
                ("csharp_indent_case_contents", indentContents ? "true" : "false"),
                ("dress_binary_expressions_layout", "always_multi")
            };
        var result = Format(source.Replace("\t", ""), preferences);
        result.ShouldBe(source);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Wrapped_headers_and_braces_follow_their_unbraced_owner(string lineEnding)
    {
        var source = """
            while (ready)
                if (firstCondition
                    && secondCondition)
                {
                    Work();
                }
                else if (thirdCondition
                    && fourthCondition)
                {
                    Other();
                }
            """.ReplaceLineEndings(lineEnding);
        var preferences = new[]
            {
                ("csharp_indent_block_contents", "true"),
                ("csharp_indent_braces", "false"),
                ("dress_binary_expressions_layout", "auto"),
                ("max_line_length", "35")
            };
        var result = Format(source, preferences);
        result.ShouldBe(source);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("if (ready)")]
    [InlineData("while (ready)")]
    [InlineData("using (resource)")]
    [InlineData("lock (gate)")]
    public void Wrapped_body_expressions_follow_their_statement_owner(string header)
    {
        var source = $$"""
            {{header}}
                result = firstCondition
                    && secondCondition;
            """;
        var preferences = new[]
            {
                ("csharp_indent_block_contents", "true"),
                ("dress_binary_expressions_layout", "always_multi")
            };
        var result = Format(source, preferences);
        result.ShouldBe(source);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void A_lambda_argument_keeps_its_continuations_when_wrapping_moves_it()
    {
        const string source = """
            class C
            {
                void M()
                {
                    assertionChain
                        .BecauseOf(because, becauseArgs)
                        .WithExpectation("Expected type to be {0}{reason}, but it was something else entirely.", typeof(TExpectation).FullName, chain => chain
                            .ForCondition(Subject is not null)
                            .FailWith("but found a null element."));
                }
            }
            """;
        const string expected = """
            class C
            {
                void M()
                {
                    assertionChain
                        .BecauseOf(because, becauseArgs)
                        .WithExpectation(
                            "Expected type to be {0}{reason}, but it was something else entirely.",
                            typeof(TExpectation).FullName,
                            chain => chain
                                .ForCondition(Subject is not null)
                                .FailWith("but found a null element."));
                }
            }
            """;
        var preferences = PreferenceCatalog.Defaults
            .Select(item => (item.Key.ToName(), item.Key == RuleKey.MaxLineLength ? "120" : item.Default))
            .ToArray();

        var result = Format(source, preferences);

        result.ShouldBe(expected);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Arguments_of_a_chained_call_indent_from_their_link(string lineEnding)
    {
        var source = """
            class C
            {
                void M()
                {
                    return _connection.Read<(int Id, string Text, DateTime CreationDate, DateTime LastChangeDate, int? Counter1, int? Counter2)>("select * from Posts where Id = @Id and IsDeleted = 0", i).First();
                }
            }
            """.ReplaceLineEndings(lineEnding);
        var expected = """
            class C
            {
                void M()
                {
                    return _connection
                        .Read<(int Id, string Text, DateTime CreationDate, DateTime LastChangeDate, int? Counter1, int? Counter2)>(
                            "select * from Posts where Id = @Id and IsDeleted = 0",
                            i
                        )
                        .First();
                }
            }
            """.ReplaceLineEndings(lineEnding);
        var preferences = PreferenceCatalog.Defaults
            .Select(item => (item.Key.ToName(), item.Key switch
                {
                    RuleKey.MaxLineLength => "160",
                    RuleKey.DressArgumentsClosingDelimiterPosition => "own_line",
                    _ => item.Default
                }))
            .ToArray();
        var result = Format(source, preferences);
        result.ShouldBe(expected);
        Format(result, preferences).ShouldBe(result);
    }
}
