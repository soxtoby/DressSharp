using System.Collections.Immutable;
using DressSharp.Architecture;

namespace DressSharp.Rules;

sealed class IndentationRule(RuleKey key, string caption, string? subgroupName, ImmutableArray<string> values, string defaultValue) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new()
        {
            RuleKey = key,
            Caption = caption,
            ExpandedCaption = RuleMetadata.Humanize(key.ToName()),
            GroupName = "Indentation",
            SubgroupName = subgroupName,
            Description = "Controls syntax indentation. Only indentation whitespace changes.",
            Values = RuleValues.From(values),
            DefaultValue = defaultValue,
            Example = ExampleFor(key),
            ExamplePreferences = CompanionsFor(key),
            OwnedSyntax = "syntax indentation",
            Invariant = "Only indentation whitespace changes"
        };

    // Each example is laid out conventionally except where its own rule decides, so its diff shows only that rule.
    static string ExampleFor(RuleKey key) => key switch
        {
            RuleKey.CSharpIndentLabels => "class Example\n{\n    void Run()\n    {\n        label:\n        Work();\n    }\n}",
            RuleKey.CSharpIndentBlockContents => "if (ready)\n{\nWork();\n}",
            RuleKey.CSharpIndentBraces => "if (ready)\n{\n    Work();\n}",
            RuleKey.CSharpIndentSwitchLabels => "switch (value)\n{\ncase 1:\n    Work();\n    break;\n}",
            RuleKey.CSharpIndentCaseContents => "switch (value)\n{\n    case 1:\n    Work();\n    break;\n}",
            _ => "switch (value)\n{\n    case 1:\n    {\n        Work();\n        break;\n    }\n}"
        };

    // A rule indents from the levels its neighbours set, and an unset neighbour leaves its lines where they are,
    // so each example sets the neighbours whose lines it moves.
    static ImmutableDictionary<RuleKey, string> CompanionsFor(RuleKey key) => key switch
        {
            RuleKey.CSharpIndentBraces => ImmutableDictionary<RuleKey, string>.Empty.Add(RuleKey.CSharpIndentBlockContents, "true"),
            RuleKey.CSharpIndentSwitchLabels => ImmutableDictionary<RuleKey, string>.Empty.Add(RuleKey.CSharpIndentCaseContents, "true"),
            RuleKey.CSharpIndentCaseContents => ImmutableDictionary<RuleKey, string>.Empty.Add(RuleKey.CSharpIndentSwitchLabels, "true"),
            RuleKey.CSharpIndentCaseContentsWhenBlock => ImmutableDictionary<RuleKey, string>.Empty.Add(RuleKey.CSharpIndentSwitchLabels, "true")
                .Add(RuleKey.CSharpIndentBlockContents, "true"),
            _ => ImmutableDictionary<RuleKey, string>.Empty
        };
}
