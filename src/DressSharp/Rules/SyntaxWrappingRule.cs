using DressSharp.Architecture;

namespace DressSharp.Rules;

sealed class SyntaxWrappingRule(RuleKey key, SyntaxWrappingKind kind) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new(
        key,
        ["always_single", "auto", "always_multi"],
        kind.ToString(),
        "Only whitespace owned by syntax wrapping changes");

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
