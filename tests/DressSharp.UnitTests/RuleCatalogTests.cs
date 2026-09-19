using DressSharp.Architecture;
using DressSharp.Configuration;
using DressSharp.Rules;
using EasyAssertions;
using Xunit;

namespace DressSharp.UnitTests;

public class RuleCatalogTests
{
    [Fact]
    public void Catalog_version_is_explicit() => RuleCatalog.BuiltIn.Version.ShouldBe(4);

    [Fact]
    public void Expanded_caption_defaults_to_caption_unless_specified()
    {
        var metadata = new NamespaceStyleRule().Metadata with { Caption = "Namespaces" };
        metadata.ExpandedCaption.ShouldBe("Namespaces");

        var expanded = metadata with { ExpandedCaption = "Namespace declaration style" };
        expanded.Caption.ShouldBe("Namespaces");
        expanded.ExpandedCaption.ShouldBe("Namespace declaration style");
    }

    [Fact]
    public void Every_supported_preference_has_exactly_one_rule()
    {
        RuleCatalog.BuiltIn.Rules.Select(rule => rule.Metadata.RuleKey).Order().ToArray()
            .ShouldMatch(Enum.GetValues<RuleKey>().Order().ToArray());
    }

    [Fact]
    public void Every_rule_exposes_complete_metadata()
    {
        foreach (var metadata in RuleCatalog.BuiltIn.Rules.Select(rule => rule.Metadata))
        {
            string.IsNullOrWhiteSpace(metadata.ExpandedCaption).ShouldBe(false);
            string.IsNullOrWhiteSpace(metadata.Caption).ShouldBe(false);
            string.IsNullOrWhiteSpace(metadata.GroupName).ShouldBe(false);
            string.IsNullOrWhiteSpace(metadata.Description).ShouldBe(false);
            string.IsNullOrWhiteSpace(metadata.Example).ShouldBe(false);
            metadata.Accepts(metadata.DefaultValue).ShouldBe(true);
        }
    }

    [Fact]
    public void Expanded_captions_omit_configuration_key_prefixes()
    {
        foreach (var caption in RuleCatalog.BuiltIn.Rules.Select(rule => rule.Metadata.ExpandedCaption))
        {
            caption.StartsWith("Csharp ", StringComparison.Ordinal).ShouldBe(false);
            caption.StartsWith("Dotnet ", StringComparison.Ordinal).ShouldBe(false);
            caption.StartsWith("Dress ", StringComparison.Ordinal).ShouldBe(false);
        }
    }

    [Fact]
    public void Non_syntax_preferences_are_rules_without_formatting_implementations()
    {
        RuleKey[] expected =
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

        RuleCatalog.BuiltIn.Rules.Where(rule => rule is not IFormattingRule).Select(rule => rule.Metadata.RuleKey).ToArray()
            .ShouldMatch(expected);
    }

    [Fact]
    public void Defaults_are_projected_from_rule_metadata()
    {
        PreferenceCatalog.Defaults.ToArray().ShouldMatch(
            RuleCatalog.BuiltIn.Rules.Select(rule => (RuleKey: rule.Metadata.RuleKey, rule.Metadata.DefaultValue)).ToArray());
    }

    [Fact]
    public void Rule_metadata_validates_open_closed_selection_and_permutation_values()
    {
        var rules = RuleCatalog.BuiltIn.Rules.ToDictionary(rule => rule.Metadata.RuleKey);

        rules[RuleKey.DressBlankLinesBetweenMembers].Metadata.Accepts("12").ShouldBe(true);
        rules[RuleKey.DressBlankLinesBetweenMembers].Metadata.Accepts("-1").ShouldBe(false);
        rules[RuleKey.DressNamespaceStyle].Metadata.Accepts("file_scoped").ShouldBe(true);
        rules[RuleKey.DressNamespaceStyle].Metadata.Accepts("invalid").ShouldBe(false);
        rules[RuleKey.CSharpNewLineBeforeOpenBrace].Metadata.Accepts("methods,properties").ShouldBe(true);
        rules[RuleKey.CSharpNewLineBeforeOpenBrace].Metadata.Accepts("methods,wat").ShouldBe(false);
        rules[RuleKey.DressUsingKindOrder].Metadata.Accepts("alias,ordinary,static").ShouldBe(true);
        rules[RuleKey.DressUsingKindOrder].Metadata.Accepts("ordinary,ordinary,static").ShouldBe(false);
    }

    [Fact]
    public void Initializer_layout_rules_expose_their_distinct_modes()
    {
        RuleKey[] keys =
            [
                RuleKey.DressObjectInitializerLayout,
                RuleKey.DressCollectionInitializerLayout,
                RuleKey.DressArrayInitializerLayout,
                RuleKey.DressWithInitializerLayout
            ];

        var rules = RuleCatalog.BuiltIn.Rules.ToDictionary(rule => rule.Metadata.RuleKey);
        keys.AllItemsSatisfy(key =>
            {
                rules[key].Metadata.AcceptedValueForms.ShouldMatch(["compact", "auto", "expanded"]);
                rules[key].Metadata.DefaultValue.ShouldBe("auto");
            });
    }

    [Fact]
    public void Binary_expression_indentation_exposes_flat_and_precedence_modes()
    {
        var metadata = RuleCatalog.BuiltIn.Rules
            .Single(rule => rule.Metadata.RuleKey == RuleKey.DressBinaryExpressionIndentation)
            .Metadata;
        metadata.AcceptedValueForms.ShouldMatch(["flat", "precedence"]);
        metadata.DefaultValue.ShouldBe("precedence");
    }
}
