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

    static InteractivePreference Preference(RuleKey key, string value) =>
        new(key, PreferenceAssignment.Explicit(value), PreferenceAssignment.Absent, null, null, null);
}
