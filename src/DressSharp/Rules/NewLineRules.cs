using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

sealed class NewLineRule(string key, NewLineKind kind, ImmutableArray<string> values) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new(
        key,
        values,
        "owned token boundaries",
        "Only boundary whitespace changes");

    internal NewLineKind Kind => kind;
    internal bool RequiresLaterElement => kind is NewLineKind.ObjectInitializerMembers or NewLineKind.AnonymousTypeMembers;
    internal ImmutableArray<SyntaxKind> EmitterTriggerKinds { get; } = kind switch
        {
            NewLineKind.OpenBrace => [SyntaxKind.OpenBraceToken],
            NewLineKind.Else => [SyntaxKind.ElseKeyword],
            NewLineKind.Catch => [SyntaxKind.CatchKeyword],
            NewLineKind.Finally => [SyntaxKind.FinallyKeyword],
            NewLineKind.QueryClauses =>
                [
                    SyntaxKind.FromKeyword,
                    SyntaxKind.LetKeyword,
                    SyntaxKind.WhereKeyword,
                    SyntaxKind.JoinKeyword,
                    SyntaxKind.OrderByKeyword,
                    SyntaxKind.SelectKeyword,
                    SyntaxKind.GroupKeyword
                ],
            _ => []
        };

    internal bool? ClaimsBreakBefore(
        SyntaxToken token,
        BraceCategories categories,
        SyntaxNode? laterElement) => kind switch
        {
            NewLineKind.OpenBrace => token.IsKind(SyntaxKind.OpenBraceToken) && BraceCategory(token) is { } category
                ? categories.Contains(category)
                : null,
            NewLineKind.Else => token.IsKind(SyntaxKind.ElseKeyword) ? categories.Enabled : null,
            NewLineKind.Catch => token.IsKind(SyntaxKind.CatchKeyword) ? categories.Enabled : null,
            NewLineKind.Finally => token.IsKind(SyntaxKind.FinallyKeyword) ? categories.Enabled : null,
            NewLineKind.ObjectInitializerMembers => laterElement is InitializerExpressionSyntax initializer
                && initializer.IsKind(SyntaxKind.ObjectInitializerExpression)
                    ? categories.Enabled
                    : null,
            NewLineKind.AnonymousTypeMembers => laterElement is AnonymousObjectCreationExpressionSyntax
                ? categories.Enabled
                : null,
            NewLineKind.QueryClauses => StartsQueryClause(token) ? categories.Enabled : null,
            _ => null
        };

    internal static SyntaxNode? StartsLaterElement(SyntaxToken token)
    {
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            if (node.SpanStart != token.SpanStart)
                return null;
            var owner = node.Parent;
            var elements = owner switch
                {
                    InitializerExpressionSyntax initializer => (IReadOnlyList<SyntaxNode>)initializer.Expressions,
                    AnonymousObjectCreationExpressionSyntax anonymous => anonymous.Initializers,
                    _ => null
                };
            if (elements is not null)
                return elements.Count > 0 && !ReferenceEquals(elements[0], node) ? owner : null;
        }

        return null;
    }

    static bool StartsQueryClause(SyntaxToken token)
    {
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            if (node.SpanStart != token.SpanStart)
                return false;
            if (node.Parent is QueryBodySyntax body)
                return body.Clauses.Contains(node) || ReferenceEquals(body.SelectOrGroup, node);
        }

        return false;
    }

    internal sealed class BraceCategories
    {
        readonly HashSet<string>? _selected;

        BraceCategories(bool enabled, HashSet<string>? selected)
        {
            Enabled = enabled;
            _selected = selected;
        }

        internal bool Enabled { get; }
        internal bool Contains(string category) => _selected is null ? Enabled : _selected.Contains(category);

        internal static BraceCategories From(NewLineKind kind, string preference)
        {
            if (kind != NewLineKind.OpenBrace)
                return new(preference.Equals("true", StringComparison.OrdinalIgnoreCase), null);
            if (preference.Equals("all", StringComparison.OrdinalIgnoreCase))
                return new(true, null);
            if (preference.Equals("none", StringComparison.OrdinalIgnoreCase))
                return new(false, null);
            return new(false, [.. preference.Split(',', StringSplitOptions.TrimEntries)]);
        }
    }

    static string? BraceCategory(SyntaxToken token) => token.Parent switch
        {
            AccessorListSyntax { Parent: EventDeclarationSyntax } => "events",
            AccessorListSyntax { Parent: IndexerDeclarationSyntax } => "indexers",
            AccessorListSyntax { Parent: PropertyDeclarationSyntax } => "properties",
            AnonymousObjectCreationExpressionSyntax => "anonymous_types",
            BlockSyntax { Parent: AccessorDeclarationSyntax } => "accessors",
            BlockSyntax { Parent: AnonymousMethodExpressionSyntax } => "anonymous_methods",
            BlockSyntax { Parent: BaseMethodDeclarationSyntax } => "methods",
            BlockSyntax { Parent: LocalFunctionStatementSyntax } => "local_functions",
            BlockSyntax { Parent: ParenthesizedLambdaExpressionSyntax or SimpleLambdaExpressionSyntax } => "lambdas",
            BlockSyntax => "control_blocks",
            BaseTypeDeclarationSyntax => "types",
            InitializerExpressionSyntax => "object_collection_array_initializers",
            _ => null
        };
}

enum NewLineKind
{
    OpenBrace,
    Else,
    Catch,
    Finally,
    ObjectInitializerMembers,
    AnonymousTypeMembers,
    QueryClauses
}
