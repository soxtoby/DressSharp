using DressSharp.Architecture;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

sealed class InitializerIndentationRule(RuleKey key, string caption, string? subgroupName, InitializerKind kind, string defaultValue) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new()
        {
            RuleKey = key,
            Caption = caption,
            ExpandedCaption = RuleMetadata.Humanize(key.ToName()),
            GroupName = "Indentation",
            SubgroupName = subgroupName,
            Description = kind switch
                {
                    InitializerKind.CollectionExpression => "Controls multiline non-empty collection expression delimiters outside arguments, including assignments and returns.",
                    InitializerKind.CollectionExpressionArgument => "Controls multiline non-empty collection expression delimiters when the expression is an argument, including named arguments.",
                    _ => "Controls multiline non-empty initializer delimiters. Only delimiter indentation changes."
                },
            Values = RuleValues.From(["indented", "not_indented"]),
            DefaultValue = defaultValue,
            Example = kind switch
                {
                    InitializerKind.Collection => "var values = new List<int>\n{\n    1,\n    2\n};",
                    InitializerKind.Array => "var values = new[]\n{\n    1,\n    2\n};",
                    InitializerKind.With => "var value = original with\n{\n    First = 1,\n    Second = 2\n};",
                    InitializerKind.CollectionExpression => """
                        class Example
                        {
                            string[] Values =
                            [
                                "first",
                                "second"
                            ];
                        }
                        """,
                    InitializerKind.CollectionExpressionArgument => """
                        class Example
                        {
                            void M() => Process(
                            [
                                "first",
                                "second"
                            ]);
                        }
                        """,
                    _ => """
        class Example
        {
            object Value = new Example
            {
                Number = 1
            };
            int Number { get; set; }
        }
        """
                },
            OwnedSyntax = "multiline non-empty initializer delimiters",
            Invariant = "Only delimiter indentation changes"
        };

    internal InitializerKind Kind => kind;

    internal static Delimiter? FindDelimiter(SyntaxToken token)
    {
        if (token.Parent is AnonymousObjectCreationExpressionSyntax { Initializers.Count: > 0 } anonymousObject
            && (token == anonymousObject.OpenBraceToken || token == anonymousObject.CloseBraceToken))
        {
            return new(anonymousObject, InitializerKind.Object, token == anonymousObject.OpenBraceToken);
        }

        if (token.Parent is InitializerExpressionSyntax { Expressions.Count: > 0 } initializer
            && (token == initializer.OpenBraceToken || token == initializer.CloseBraceToken))
        {
            var kind = KindOf(initializer);
            return kind is { } value
                ? new(initializer, value, token == initializer.OpenBraceToken)
                : null;
        }

        if (token.Parent is CollectionExpressionSyntax { Elements.Count: > 0 } collection
            && (token == collection.OpenBracketToken || token == collection.CloseBracketToken))
        {
            return new(collection, CollectionKind(collection), token == collection.OpenBracketToken);
        }

        return null;
    }

    internal static InitializerKind? KindOf(InitializerExpressionSyntax initializer) => initializer.Kind() switch
        {
            SyntaxKind.ObjectInitializerExpression => InitializerKind.Object,
            SyntaxKind.CollectionInitializerExpression => InitializerKind.Collection,
            SyntaxKind.ArrayInitializerExpression => InitializerKind.Array,
            SyntaxKind.WithInitializerExpression => InitializerKind.With,
            _ => null
        };

    internal static InitializerKind? KindOf(SyntaxNode initializer) => initializer switch
        {
            AnonymousObjectCreationExpressionSyntax => InitializerKind.Object,
            InitializerExpressionSyntax expression => KindOf(expression),
            CollectionExpressionSyntax collection => CollectionKind(collection),
            _ => null
        };

    static InitializerKind CollectionKind(CollectionExpressionSyntax collection)
    {
        SyntaxNode expression = collection;
        while (expression.Parent is ParenthesizedExpressionSyntax or CastExpressionSyntax)
            expression = expression.Parent;
        return expression.Parent is ArgumentSyntax
            ? InitializerKind.CollectionExpressionArgument
            : InitializerKind.CollectionExpression;
    }

    internal readonly record struct Delimiter(SyntaxNode Initializer, InitializerKind Kind, bool IsOpening);
}

enum InitializerKind
{
    Object,
    Collection,
    Array,
    With,
    CollectionExpression,
    CollectionExpressionArgument
}
