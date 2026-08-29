using DressSharp.Architecture;

namespace DressSharp.Rules;

sealed class SyntaxWrappingRule(RuleKey key, SyntaxWrappingKind kind, string defaultValue) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        RuleKey = key,
        Name = RuleMetadata.Humanize(key.ToName()),
        GroupName = "Wrapping",
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
