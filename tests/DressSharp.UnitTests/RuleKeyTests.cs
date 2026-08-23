using DressSharp.Architecture;
using Xunit;

namespace DressSharp.UnitTests;

public sealed class RuleKeyTests
{
    [Fact]
    public void Every_enum_member_round_trips_through_its_editorconfig_name()
    {
        foreach (var key in Enum.GetValues<RuleKey>())
        {
            Assert.True(RuleKeys.TryParse(key.ToName(), out var parsed));
            Assert.Equal(key, parsed);
        }
    }

    [Theory]
    [InlineData(RuleKey.CSharpPreferredModifierOrder, "csharp_preferred_modifier_order")]
    [InlineData(RuleKey.CSharpNewLineBeforeMembersInObjectInitializers, "csharp_new_line_before_members_in_object_initializers")]
    [InlineData(RuleKey.DotnetSortSystemDirectivesFirst, "dotnet_sort_system_directives_first")]
    [InlineData(RuleKey.DressMaxConsecutiveBlankLines, "dress_max_consecutive_blank_lines")]
    [InlineData(RuleKey.InsertFinalNewline, "insert_final_newline")]
    internal void Names_convert_to_the_expected_editorconfig_spelling(RuleKey key, string expected) =>
        Assert.Equal(expected, key.ToName());

    [Fact]
    public void Parsing_is_case_insensitive_and_rejects_unknown_names()
    {
        Assert.True(RuleKeys.TryParse("DRESS_NAMESPACE_STYLE", out var parsed));
        Assert.Equal(RuleKey.DressNamespaceStyle, parsed);
        Assert.False(RuleKeys.TryParse("dress_not_a_preference", out _));
        Assert.Throws<ArgumentException>(() => RuleKeys.Parse("dress_not_a_preference"));
    }
}
