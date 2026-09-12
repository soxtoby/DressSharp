using DressSharp.Architecture;

namespace DressSharp.Rules;

sealed class SyntaxWrappingRule(
    RuleKey key,
    string caption,
    string? subgroupName,
    SyntaxWrappingKind kind,
    string defaultValue) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        RuleKey = key,
        Caption = caption,
        ExpandedCaption = RuleMetadata.Humanize(key.ToName()),
        GroupName = "Wrapping",
        SubgroupName = subgroupName,
        Description = $"Controls {kind}. Only whitespace owned by syntax wrapping changes."
            + (kind is SyntaxWrappingKind.Arguments or SyntaxWrappingKind.Parameters or SyntaxWrappingKind.CollectionExpressions
                ? " Auto puts each item in a multiline list on its own line and keeps fitting single-line lists compact."
                : kind == SyntaxWrappingKind.ConditionalExpressions
                    ? " Wrapped branches indent one level beyond the deepest formatted line of a multiline condition."
                    : ""),
        Values = RuleValues.From(InitializerKindFor(kind) is not null
            ? ["compact", "auto", "expanded"]
            : ["always_single", "auto", "always_multi"]),
        DefaultValue = defaultValue,
        Example = InitializerKindFor(kind) is not null
            ? """
                class Example
                {
                    Example Value = new Example { Number = 1 };
                    int Number { get; set; }
                }
                """
            : """
                class Example
                {
                    void Run() { Call(firstArgument, secondArgument, thirdArgument); }
                }
                """,
        OwnedSyntax = kind.ToString(),
        Invariant = "Only whitespace owned by syntax wrapping changes"
    };

    internal SyntaxWrappingKind Kind => kind;
    internal bool IsInitializerLayout => InitializerKindFor(kind) is not null;

    internal static InitializerKind? InitializerKindFor(SyntaxWrappingKind value) => value switch
    {
        SyntaxWrappingKind.ObjectInitializers => InitializerKind.Object,
        SyntaxWrappingKind.CollectionInitializers => InitializerKind.Collection,
        SyntaxWrappingKind.ArrayInitializers => InitializerKind.Array,
        SyntaxWrappingKind.WithInitializers => InitializerKind.With,
        _ => null
    };

    internal static SyntaxWrappingKind? KindFor(InitializerKind value) => value switch
    {
        InitializerKind.Object => SyntaxWrappingKind.ObjectInitializers,
        InitializerKind.Collection => SyntaxWrappingKind.CollectionInitializers,
        InitializerKind.Array => SyntaxWrappingKind.ArrayInitializers,
        InitializerKind.With => SyntaxWrappingKind.WithInitializers,
        _ => null
    };
}

enum SyntaxWrappingKind
{
    Arguments,
    Parameters,
    ObjectInitializers,
    CollectionInitializers,
    ArrayInitializers,
    WithInitializers,
    CollectionExpressions,
    BaseTypeLists,
    ConstraintClauses,
    MemberAccessChains,
    BinaryExpressions,
    ConditionalExpressions,
    QueryClauses,
    Attributes
}
