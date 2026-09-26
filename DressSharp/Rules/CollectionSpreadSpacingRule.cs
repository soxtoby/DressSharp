using System.Collections.Immutable;
using DressSharp.Architecture;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

sealed class CollectionSpreadSpacingRule() : TokenSpacingRule(
    RuleKey.DressSpaceAfterCollectionSpreadOperator,
    "After collection spread operator",
    "Collection expressions",
    ["true", "false"],
    "true",
    "spacing after collection-expression spread operators",
    """
    class Example
    {
        int[] Copy(int[] values) => [.. values];
    }
    """)
{
    internal override ImmutableArray<SyntaxKind> TriggerKinds { get; } = [SyntaxKind.DotDotToken];

    internal override bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string preference) =>
        left.IsKind(SyntaxKind.DotDotToken)
        && left.Parent is SpreadElementSyntax spread
        && left == spread.OperatorToken
            ? preference == "true"
            : null;
}
