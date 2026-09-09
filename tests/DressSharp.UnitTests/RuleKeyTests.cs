using DressSharp.Architecture;
using Xunit;
using EasyAssertions;

namespace DressSharp.UnitTests;

public sealed class RuleKeyTests
{
    [Fact]
    public void Every_enum_member_round_trips_through_its_editorconfig_name()
    {
        foreach (var key in Enum.GetValues<RuleKey>())
        {
            RuleKeys.TryParse(key.ToName(), out var parsed).ShouldBe(true);
            parsed.ShouldBe(key);
        }
    }

    [Theory]
    [InlineData(RuleKey.CSharpPreferredModifierOrder, "csharp_preferred_modifier_order")]
    [InlineData(RuleKey.CSharpNewLineBeforeMembersInObjectInitializers, "csharp_new_line_before_members_in_object_initializers")]
    [InlineData(RuleKey.DotnetSortSystemDirectivesFirst, "dotnet_sort_system_directives_first")]
    [InlineData(RuleKey.DressMaxConsecutiveBlankLines, "dress_max_consecutive_blank_lines")]
    [InlineData(RuleKey.DressObjectInitializerLayout, "dress_object_initializer_layout")]
    [InlineData(RuleKey.DressCollectionInitializerLayout, "dress_collection_initializer_layout")]
    [InlineData(RuleKey.DressArrayInitializerLayout, "dress_array_initializer_layout")]
    [InlineData(RuleKey.DressWithInitializerLayout, "dress_with_initializer_layout")]
    [InlineData(RuleKey.DressBinaryExpressionIndentation, "dress_binary_expression_indentation")]
    [InlineData(RuleKey.DressSwitchExpressionIndentation, "dress_switch_expression_indentation")]
    [InlineData(RuleKey.InsertFinalNewline, "insert_final_newline")]
    internal void Names_convert_to_the_expected_editorconfig_spelling(RuleKey key, string expected) =>
        key.ToName().ShouldBe(expected);

    [Fact]
    public void Parsing_is_case_insensitive_and_rejects_unknown_names()
    {
        RuleKeys.TryParse("DRESS_NAMESPACE_STYLE", out var parsed).ShouldBe(true);
        parsed.ShouldBe(RuleKey.DressNamespaceStyle);
        RuleKeys.TryParse("dress_not_a_preference", out _).ShouldBe(false);
        Should.Throw<ArgumentException>(() => RuleKeys.Parse("dress_not_a_preference"));
    }
}
