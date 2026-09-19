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
            Example = key == RuleKey.CSharpIndentLabels
                ? "class Example\n{\n    void Run()\n    {\n        label:\n        Work();\n    }\n}"
                : """
                class Example
                {
                void Run()
                {
                label:
                switch (value)
                {
                case 1:
                Work();
                break;
                case 2:
                {
                Work();
                break;
                }
                }
                }
                void Work() { }
                }
                """,
            OwnedSyntax = "syntax indentation",
            Invariant = "Only indentation whitespace changes"
        };
}
