using DressSharp.Architecture;

namespace DressSharp.Rules;

sealed class SyntaxWrappingRule(RuleKey key, string caption, string? subgroupName, SyntaxWrappingKind kind, string defaultValue) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        RuleKey = key,
        Caption = caption,
        ExpandedCaption = RuleMetadata.Humanize(key.ToName()),
        GroupName = "Wrapping",
        SubgroupName = subgroupName,
        Description = $"Controls {kind}. Only whitespace owned by syntax wrapping changes.",
        Values = RuleValues.From(["always_single", "auto", "always_multi"]),
        DefaultValue = defaultValue,
        Example = """
            class Example
            {
                void Run() { Call(firstArgument, secondArgument, thirdArgument); }
            }
            """,
        OwnedSyntax = kind.ToString(),
        Invariant = "Only whitespace owned by syntax wrapping changes"
    };

    internal SyntaxWrappingKind Kind => kind;
}

enum SyntaxWrappingKind
{
    Arguments,
    Parameters,
    Initializers,
    CollectionExpressions,
    BaseTypeLists,
    ConstraintClauses,
    MemberAccessChains,
    BinaryExpressions,
    ConditionalExpressions,
    QueryClauses,
    Attributes
}
