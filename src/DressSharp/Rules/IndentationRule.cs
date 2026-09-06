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
        Example = """
            class Example
            {
            void Run()
            {
            if (true)
            Work();
            }
            void Work() { }
            }
            """,
        OwnedSyntax = "syntax indentation",
        Invariant = "Only indentation whitespace changes"
    };
}
