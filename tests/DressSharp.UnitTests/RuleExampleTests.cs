using DressSharp.Architecture;
using DressSharp.Configuration;
using DressSharp.Interactive;
using DressSharp.Rules;
using EasyAssertions;
using Xunit;

namespace DressSharp.UnitTests;

public class RuleExampleTests
{
    public static IEnumerable<object[]> Rules() => RuleCatalog.BuiltIn.Rules.Select(rule => new object[] { rule.Metadata.RuleKey.ToName() });

    [Theory]
    [MemberData(nameof(Rules))]
    public async Task Example_demonstrates_a_difference_between_values(string key)
    {
        var metadata = RuleCatalog.BuiltIn.Rules.Single(rule => rule.Metadata.RuleKey.ToName() == key).Metadata;
        var values = metadata.Values.Kind switch
            {
                RuleValueKind.Integer => new[] { metadata.Values.Minimum!.Value.ToString(), "2", "8", "40", metadata.DefaultValue },
                RuleValueKind.Permutation => new[] { metadata.DefaultValue, string.Join(",", metadata.DefaultValue.Split(',').Reverse()) },
                _ => metadata.Values.Options.Select(option => option.Value).Concat(metadata.Values.SpecialValues.IsDefault ? [] : metadata.Values.SpecialValues)
            };
        var results = new HashSet<string>();
        foreach (var value in values.Distinct())
        {
            var preferences = metadata.ExamplePreferences.Select(pair => Preference(pair.Key, pair.Value))
                .Append(Preference(metadata.RuleKey, value)).ToArray();
            var result = await InteractivePreview.Format(metadata.Example, preferences, TestContext.Current.CancellationToken);
            result.SkippedOccurrences.ShouldBe(0);
            results.Add($"{result.Text}|{result.Encoding}|{result.LineEndings}|{result.FinalNewline}");
        }
        results.Count.ShouldBeGreaterThan(1);
    }

    public static IEnumerable<object[]> MultipleChoiceRules() => RuleCatalog.BuiltIn.Rules
        .Where(rule => rule.Metadata.Values.Kind == RuleValueKind.MultipleChoice)
        .Select(rule => new object[] { rule.Metadata.RuleKey.ToName() });

    [Theory]
    [MemberData(nameof(MultipleChoiceRules))]
    public async Task Example_demonstrates_every_option_of_a_multiple_choice_rule(string key)
    {
        var metadata = RuleCatalog.BuiltIn.Rules.Single(rule => rule.Metadata.RuleKey.ToName() == key).Metadata;
        var outcomes = new Dictionary<string, string>();
        foreach (var value in metadata.Values.Options.Select(option => option.Value).Concat(metadata.Values.SpecialValues))
        {
            var preferences = metadata.ExamplePreferences.Select(pair => Preference(pair.Key, pair.Value))
                .Append(Preference(metadata.RuleKey, value)).ToArray();
            var result = await InteractivePreview.Format(metadata.Example, preferences, TestContext.Current.CancellationToken);
            outcomes[value] = result.Text;
        }
        var alike = outcomes.GroupBy(outcome => outcome.Value)
            .Where(group => group.Count() > 1)
            .Select(group => string.Join(", ", group.Select(outcome => outcome.Key)));
        alike.ShouldBeEmpty();
    }

    public static IEnumerable<object[]> OptionExamples() => RuleCatalog.BuiltIn.Rules
        .SelectMany(rule => rule.Metadata.OptionExamples.Keys.Select(option => new object[] { rule.Metadata.RuleKey.ToName(), option }));

    [Fact]
    public void Option_examples_cover_every_option()
    {
        foreach (var metadata in RuleCatalog.BuiltIn.Rules.Select(rule => rule.Metadata).Where(metadata => !metadata.OptionExamples.IsEmpty))
            metadata.OptionExamples.Keys.Order().ShouldMatch(metadata.Values.Options.Select(option => option.Value).Order());
    }

    [Theory]
    [MemberData(nameof(OptionExamples))]
    public async Task Option_example_changes_under_its_option(string key, string option)
    {
        var metadata = RuleCatalog.BuiltIn.Rules.Single(rule => rule.Metadata.RuleKey.ToName() == key).Metadata;
        var example = metadata.OptionExamples[option];
        var preferences = metadata.ExamplePreferences.Select(pair => Preference(pair.Key, pair.Value))
            .Append(Preference(metadata.RuleKey, option)).ToArray();
        var result = await InteractivePreview.Format(example, preferences, TestContext.Current.CancellationToken);
        result.SkippedOccurrences.ShouldBe(0);
        result.Text.ShouldNotBe(example);
    }

    static InteractivePreference Preference(RuleKey key, string value) =>
        new(key, PreferenceAssignment.Explicit(value), PreferenceAssignment.Absent, null, null, null);
}
