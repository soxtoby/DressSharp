using DressSharp.Architecture;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

sealed class InitializerIndentationRule(RuleKey key, InitializerKind kind, string defaultValue) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        RuleKey = key,
        Name = RuleMetadata.Humanize(key.ToName()),
        GroupName = "Indentation",
        Description = "Controls multiline non-empty initializer delimiters. Only delimiter indentation changes.",
        Values = RuleValues.From(["indented", "not_indented"]),
        DefaultValue = defaultValue,
        Example = """
        class Example
        {
            object Value = new Example
            {
                Number = 1
            };
            int Number { get; set; }
        }
        """,
        OwnedSyntax = "multiline non-empty initializer delimiters",
        Invariant = "Only delimiter indentation changes"
    };

    internal InitializerKind Kind => kind;

    internal static Delimiter? FindDelimiter(SyntaxToken token)
    {
        if (token.Parent is InitializerExpressionSyntax { Expressions.Count: > 0 } initializer
            && (token == initializer.OpenBraceToken || token == initializer.CloseBraceToken))
        {
            var kind = initializer.Kind() switch
            {
                SyntaxKind.ObjectInitializerExpression => InitializerKind.Object,
                SyntaxKind.CollectionInitializerExpression => InitializerKind.Collection,
                SyntaxKind.ArrayInitializerExpression => InitializerKind.Array,
                SyntaxKind.WithInitializerExpression => InitializerKind.With,
                _ => (InitializerKind?)null
            };
            return kind is { } value
                ? new(initializer, value, token == initializer.OpenBraceToken)
                : null;
        }

        if (token.Parent is CollectionExpressionSyntax { Elements.Count: > 0 } collection
            && (token == collection.OpenBracketToken || token == collection.CloseBracketToken))
        {
            return new(collection, InitializerKind.CollectionExpression, token == collection.OpenBracketToken);
        }

        return null;
    }

    internal readonly record struct Delimiter(SyntaxNode Initializer, InitializerKind Kind, bool IsOpening);
}

enum InitializerKind
{
    Object,
    Collection,
    Array,
    With,
    CollectionExpression
}
