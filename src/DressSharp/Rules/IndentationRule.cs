using System.Collections.Immutable;
using DressSharp.Architecture;

namespace DressSharp.Rules;

sealed class IndentationRule(RuleKey key, ImmutableArray<string> values) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new(
        key,
        values,
        "syntax indentation",
        "Only indentation whitespace changes");
}
