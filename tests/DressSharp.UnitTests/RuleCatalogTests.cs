using DressSharp.Architecture;
using DressSharp.Rules;
using Xunit;
using EasyAssertions;

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

        actual.ShouldMatch(expected);
    }

    [Fact]
    public void Catalog_metadata_validates_open_and_closed_value_sets()
    {
        var blankLines = RuleCatalog.BuiltIn.Rules.Single(rule => rule.Metadata.RuleKey == RuleKey.DressBlankLinesBetweenMembers);
        var namespaceStyle = RuleCatalog.BuiltIn.Rules.Single(rule => rule.Metadata.RuleKey == RuleKey.DressNamespaceStyle);

        blankLines.Metadata.Accepts("0").ShouldBe(true);
        blankLines.Metadata.Accepts("12").ShouldBe(true);
        blankLines.Metadata.Accepts("-1").ShouldBe(false);
        namespaceStyle.Metadata.Accepts("file_scoped").ShouldBe(true);
        namespaceStyle.Metadata.Accepts("invalid").ShouldBe(false);
    }
}
