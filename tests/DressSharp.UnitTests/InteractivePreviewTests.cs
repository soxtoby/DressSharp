using DressSharp.Architecture;
using DressSharp.Configuration;
using DressSharp.Interactive;
using EasyAssertions;
using Xunit;

namespace DressSharp.UnitTests;

public sealed class InteractivePreviewTests
{
    [Fact]
    public async Task Disabling_inherited_placement_explains_a_return_moving_to_a_new_line()
    {
        const string source = "if (true) return false;";
        var placement = new InteractivePreference(
            RuleKey.DressEmbeddedStatementPlacement,
            PreferenceAssignment.Absent,
            PreferenceAssignment.Explicit("next_line"),
            null,
            null,
            null);
        var enabled = await InteractivePreview.Format(source, [placement], TestContext.Current.CancellationToken);
        var disabled = await InteractivePreview.Format(
            source,
            [placement with { Local = PreferenceAssignment.Unset }],
            TestContext.Current.CancellationToken);

        enabled.Text.ShouldBe("if (true)\n    return false;");
        disabled.Text.ShouldBe(source);
    }

    [Fact]
    public async Task Preview_runs_real_formatting_and_representation_preferences_in_memory()
    {
        var result = await InteractivePreview.Format(
            "class C { void M(int a,int b) {} }",
            [
                Preference(RuleKey.CSharpSpaceAfterComma, "true"),
                Preference(RuleKey.EndOfLine, "crlf"),
                Preference(RuleKey.InsertFinalNewline, "true"),
                Preference(RuleKey.Charset, "utf-16le"),
            ],
            TestContext.Current.CancellationToken);

        result.Text.ShouldContain("int a, int b");
        result.Text.EndsWith("\r\n", StringComparison.Ordinal).ShouldBe(true);
        result.Encoding.ShouldBe("utf-16le");
        result.LineEndings.ShouldBe("CRLF");
        result.FinalNewline.ShouldBe(true);
    }

    [Fact]
    public async Task Preview_decodes_with_the_selected_encoding_instead_of_guessing()
    {
        const string source = "// Ã©\n";
        var result = await InteractivePreview.Format(source, [Preference(RuleKey.Charset, "latin1")], TestContext.Current.CancellationToken);
        result.Text.ShouldBe(source);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task Preview_aligns_a_binary_operator_with_its_first_operand(string lineEnding)
    {
        var source = """
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
                                || SyntaxFacts.GetAssignmentExpression(kind) != SyntaxKind.None)
                    ];
            }
            """.ReplaceLineEndings(lineEnding);
        var preferences = new[]
            {
                Preference(RuleKey.IndentSize, "4"),
                Preference(RuleKey.CSharpIndentBlockContents, "true"),
                Preference(RuleKey.DressBinaryExpressionsLayout, "auto"),
                Preference(RuleKey.DressBinaryExpressionIndentation, "precedence"),
                Preference(RuleKey.DotnetStyleOperatorPlacementWhenWrapping, "beginning_of_line"),
                Preference(RuleKey.MaxLineLength, "160")
            };

        var result = await InteractivePreview.Format(source, preferences, TestContext.Current.CancellationToken);

        result.Text.ShouldBe(source);
        (await InteractivePreview.Format(result.Text, preferences, TestContext.Current.CancellationToken)).Text.ShouldBe(result.Text);
    }

    [Fact]
    public void Pending_assignments_resolve_inheritance_unset_and_derived_values()
    {
        var configuration = InteractiveEditorConfig.ResolvePending(
            [
                new(RuleKey.IndentSize, PreferenceAssignment.Absent, PreferenceAssignment.Explicit("tab"), null, null, null),
                Preference(RuleKey.TabWidth, "8"),
                new(RuleKey.CSharpSpaceAfterComma, PreferenceAssignment.Unset, PreferenceAssignment.Explicit("true"), null, null, null),
            ]);
        configuration.Preferences[RuleKey.IndentSize].ShouldBe("8");
        configuration.Preferences.ContainsKey(RuleKey.CSharpSpaceAfterComma).ShouldBe(false);
    }

    [Fact]
    public async Task No_preferences_preserves_source_and_empty_source_is_valid()
    {
        const string source = "class C { void M(int a,int b) {} }";
        (await InteractivePreview.Format(source, [], TestContext.Current.CancellationToken)).Text.ShouldBe(source);
        (await InteractivePreview.Format("", [], TestContext.Current.CancellationToken)).Text.ShouldBe("");
    }

    [Fact]
    public async Task Malformed_code_and_disabled_regions_remain_editable()
    {
        const string source = "#if DEBUG\nclass C { void M(int a,int b) {} }\n#endif\nclass D { void M(int a,int b) {}\n";
        var result = await InteractivePreview.Format(source, [Preference(RuleKey.CSharpSpaceAfterComma, "true")], TestContext.Current.CancellationToken);
        result.Text.ShouldContain("class C { void M(int a,int b) {} }");
        result.Text.ShouldContain("class D");
    }

    [Fact]
    public async Task Invalid_preference_does_not_poison_next_preview()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => InteractivePreview.Format("class C {}", [Preference(RuleKey.IndentSize, "bad")], TestContext.Current.CancellationToken));
        (await InteractivePreview.Format("class C {}", [], TestContext.Current.CancellationToken)).Text.ShouldBe("class C {}");
    }

    static InteractivePreference Preference(RuleKey key, string value) =>
        new(key, PreferenceAssignment.Explicit(value), PreferenceAssignment.Absent, null, null, null);
}
