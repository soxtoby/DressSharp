using DressSharp.Architecture;

namespace DressSharp.Rules;

sealed class ClosingDelimiterPositionRule(RuleKey key, SyntaxWrappingKind kind, string example) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        RuleKey = key,
        Caption = kind switch
        {
            SyntaxWrappingKind.Arguments => "Arguments",
            SyntaxWrappingKind.Parameters => "Parameters",
            SyntaxWrappingKind.ObjectInitializers => "Object initializers",
            SyntaxWrappingKind.CollectionInitializers => "Collection initializers",
            SyntaxWrappingKind.ArrayInitializers => "Array initializers",
            SyntaxWrappingKind.WithInitializers => "With initializers",
            SyntaxWrappingKind.CollectionExpressions => "Collection expressions",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        },
        ExpandedCaption = RuleMetadata.Humanize(key.ToName()),
        GroupName = "Wrapping",
        SubgroupName = "Closing delimiters",
        Description = "Places a multiline list's closing delimiter after the last item or on its own line, aligned with the opening line. Single-line lists are unchanged. Comments retain required line breaks.",
        Values = RuleValues.Choice("after_last_item", "own_line"),
        DefaultValue = "own_line",
        Example = example,
        OwnedSyntax = $"the closing boundary of multiline {kind}",
        Invariant = "Single-line lists and item layout are unchanged"
    };

    internal SyntaxWrappingKind Kind => kind;

    internal static IEnumerable<ClosingDelimiterPositionRule> All() =>
    [
        new(RuleKey.DressArgumentsClosingDelimiterPosition, SyntaxWrappingKind.Arguments, "Call(\n    first,\n    second);"),
        new(RuleKey.DressParametersClosingDelimiterPosition, SyntaxWrappingKind.Parameters, "class Example(\n    int value)\n{\n}"),
        new(RuleKey.DressObjectInitializerClosingDelimiterPosition, SyntaxWrappingKind.ObjectInitializers, "var value = new Example\n{\n    First = 1\n};"),
        new(RuleKey.DressCollectionInitializerClosingDelimiterPosition, SyntaxWrappingKind.CollectionInitializers, "var values = new List<int>\n{\n    1, 2\n};"),
        new(RuleKey.DressArrayInitializerClosingDelimiterPosition, SyntaxWrappingKind.ArrayInitializers, "var values = new[]\n{\n    1, 2\n};"),
        new(RuleKey.DressWithInitializerClosingDelimiterPosition, SyntaxWrappingKind.WithInitializers, "var value = original with\n{\n    First = 1\n};"),
        new(RuleKey.DressCollectionExpressionClosingDelimiterPosition, SyntaxWrappingKind.CollectionExpressions, "int[] values = [\n    1, 2\n];")
    ];
}
