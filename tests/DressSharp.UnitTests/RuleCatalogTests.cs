using DressSharp.Architecture;
using DressSharp.Rules;
using Xunit;

namespace DressSharp.UnitTests;

public class RuleCatalogTests
{
    [Fact]
    public void Every_formatting_preference_has_exactly_one_catalog_rule()
    {
        RuleKey[] settings =
        [
            RuleKey.Charset,
            RuleKey.EndOfLine,
            RuleKey.InsertFinalNewline,
            RuleKey.TrimTrailingWhitespace,
            RuleKey.IndentStyle,
            RuleKey.IndentSize,
            RuleKey.TabWidth,
            RuleKey.MaxLineLength
        ];
        var expected = Enum.GetValues<RuleKey>().Except(settings).Order().ToArray();
        var actual = RuleCatalog.BuiltIn.Rules.Select(rule => rule.Metadata.RuleKey).Order().ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Catalog_metadata_validates_open_and_closed_value_sets()
    {
        var blankLines = RuleCatalog.BuiltIn.Rules.Single(
            rule => rule.Metadata.RuleKey == RuleKey.DressBlankLinesBetweenMembers);
        var namespaceStyle = RuleCatalog.BuiltIn.Rules.Single(
            rule => rule.Metadata.RuleKey == RuleKey.DressNamespaceStyle);

        Assert.True(blankLines.Metadata.Accepts("0"));
        Assert.True(blankLines.Metadata.Accepts("12"));
        Assert.False(blankLines.Metadata.Accepts("-1"));
        Assert.True(namespaceStyle.Metadata.Accepts("file_scoped"));
        Assert.False(namespaceStyle.Metadata.Accepts("invalid"));
    }
}
