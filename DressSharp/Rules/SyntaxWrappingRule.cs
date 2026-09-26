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
                + (kind is SyntaxWrappingKind.Arguments or SyntaxWrappingKind.Parameters or SyntaxWrappingKind.CollectionExpressions ? " Auto puts each item in a multiline list on its own line and keeps fitting single-line lists compact."
                    : kind == SyntaxWrappingKind.ConditionalExpressions ? " Wrapped branches indent one level beyond the deepest formatted line of a multiline condition."
                    : ""),
            Values = RuleValues.From(InitializerKindFor(kind) is not null
                ? ["compact", "auto", "expanded"]
                : ["always_single", "auto", "always_multi"]),
            DefaultValue = defaultValue,
            Example = kind switch
                {
                    SyntaxWrappingKind.Parameters => "class Example { void Run(int first, int second, int third) { } }",
                    SyntaxWrappingKind.ObjectInitializers => "var value = new Example { First = 1, Second = 2 };",
                    SyntaxWrappingKind.CollectionInitializers => "var values = new List<int> { 1, 2, 3 };",
                    SyntaxWrappingKind.ArrayInitializers => "var values = new[] { 1, 2, 3 };",
                    SyntaxWrappingKind.WithInitializers => "var value = original with { First = 1, Second = 2 };",
                    SyntaxWrappingKind.CollectionExpressions => "int[] values = [1, 2, 3];",
                    SyntaxWrappingKind.BaseTypeLists => "class Example : Base, IFirst, ISecond { }",
                    SyntaxWrappingKind.ConstraintClauses => "class Example<T, U> where T : class where U : new() { }",
                    SyntaxWrappingKind.MemberAccessChains => "var value = source.Where(predicate).Select(selector).ToList();",
                    SyntaxWrappingKind.BinaryExpressions => "var value = first + second + third;",
                    SyntaxWrappingKind.ConditionalExpressions => "var value = condition ? first : second;",
                    SyntaxWrappingKind.QueryClauses => "var result = from item in items where item.Active select item.Name;",
                    SyntaxWrappingKind.Attributes => "[First, Second, Third] class Example { }",
                    _ => "Call(firstArgument, secondArgument, thirdArgument);"
                },
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
