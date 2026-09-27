using System.Collections.Immutable;
using DressSharp.Architecture;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

sealed class NewLineRule(RuleKey ruleKey, string caption, string? subgroupName, NewLineKind kind, RuleValueDefinition values, string defaultValue)
    : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new()
        {
            RuleKey = ruleKey,
            Caption = caption,
            ExpandedCaption = RuleMetadata.Humanize(ruleKey.ToName()),
            GroupName = "Braces and bodies",
            SubgroupName = subgroupName,
            Description = "Controls owned token boundaries. Only boundary whitespace changes.",
            Values = values,
            DefaultValue = defaultValue,
            Example = kind switch
                {
                    NewLineKind.Catch or NewLineKind.Finally => "try { Work(); } catch (Exception) { Recover(); } finally { CleanUp(); }",
                    NewLineKind.Else => "if (ready) { Work(); } else { Wait(); }",
                    NewLineKind.ObjectInitializerMembers => "var value = new Example { First = 1, Second = 2 };",
                    NewLineKind.AnonymousTypeMembers => "var value = new { First = 1, Second = 2 };",
                    NewLineKind.QueryClauses => "var result = from item in items where item.Active select item.Name;",
                    _ => """
                    class Example {
                        int Count {
                            get {
                                return 0;
                            }
                        }

                        int this[int index] {
                            get => index;
                        }

                        event Action Changed {
                            add => Work();
                            remove => Work();
                        }

                        void Run() {
                            if (Count > 0) {
                                Work();
                            }

                            void Local() {
                                Work();
                            }

                            Action lambda = () => {
                                Work();
                            };
                            Action method = delegate {
                                Work();
                            };
                            var point = new {
                                X = 1
                            };
                            int[] numbers = new[] {
                                1
                            };
                        }
                    }
                    """
                },
            ExamplePreferences = kind == NewLineKind.OpenBrace ? OpenBraceCompanions : ImmutableDictionary<RuleKey, string>.Empty,
            OptionExamples = kind == NewLineKind.OpenBrace ? OpenBraceExamples : ImmutableDictionary<string, string>.Empty,
            OwnedSyntax = "owned token boundaries",
            Invariant = "Only boundary whitespace changes"
        };

    // A brace this rule moves to its own line takes its column from brace indentation, so the examples show the Default one.
    static readonly ImmutableDictionary<RuleKey, string> OpenBraceCompanions =
        ImmutableDictionary<RuleKey, string>.Empty.Add(RuleKey.CSharpIndentBraces, "false");

    // Each holds only the brace its option moves, and members sit in a type so they parse as members.
    static readonly ImmutableDictionary<string, string> OpenBraceExamples = new Dictionary<string, string>
        {
            ["accessors"] = "class Example {\n    int Count {\n        get {\n            return 0;\n        }\n    }\n}",
            ["anonymous_methods"] = "Action action = delegate {\n    Work();\n};",
            ["anonymous_types"] = "var point = new {\n    X = 1\n};",
            ["control_blocks"] = "if (ready) {\n    Work();\n}",
            ["events"] = "class Example {\n    event Action Changed {\n        add => Work();\n        remove => Work();\n    }\n}",
            ["indexers"] = "class Example {\n    int this[int index] {\n        get => index;\n    }\n}",
            ["lambdas"] = "Action action = () => {\n    Work();\n};",
            ["local_functions"] = "void Local() {\n    Work();\n}",
            ["methods"] = "class Example {\n    void Run() {\n        Work();\n    }\n}",
            ["object_collection_array_initializers"] = "int[] numbers = new[] {\n    1,\n    2\n};",
            ["properties"] = "class Example {\n    int Count {\n        get => 0;\n    }\n}",
            ["types"] = "class Example {\n    int count;\n}",
        }.ToImmutableDictionary();

    internal NewLineKind Kind => kind;
    internal bool RequiresInitializerMemberBoundary => kind is NewLineKind.ObjectInitializerMembers or NewLineKind.AnonymousTypeMembers;
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
        SyntaxNode? initializerAtMemberBoundary) => kind switch
            {
                NewLineKind.OpenBrace => token.IsKind(SyntaxKind.OpenBraceToken) && BraceCategory(token) is { } category
                ? categories.Contains(category)
                : null,
                NewLineKind.Else => token.IsKind(SyntaxKind.ElseKeyword) ? categories.Enabled : null,
                NewLineKind.Catch => token.IsKind(SyntaxKind.CatchKeyword) ? categories.Enabled : null,
                NewLineKind.Finally => token.IsKind(SyntaxKind.FinallyKeyword) ? categories.Enabled : null,
                NewLineKind.ObjectInitializerMembers => initializerAtMemberBoundary is InitializerExpressionSyntax initializer
                && initializer.IsKind(SyntaxKind.ObjectInitializerExpression)
                    ? categories.Enabled
                    : null,
                NewLineKind.AnonymousTypeMembers => initializerAtMemberBoundary is AnonymousObjectCreationExpressionSyntax
                ? categories.Enabled
                : null,
                NewLineKind.QueryClauses => StartsQueryClause(token) ? categories.Enabled : null,
                _ => null
            };

    internal static SyntaxNode? InitializerAtMemberBoundary(SyntaxToken token)
    {
        if (token.Parent is InitializerExpressionSyntax { Expressions.Count: > 0 } initializer
            && token == initializer.CloseBraceToken)
        {
            return initializer;
        }

        if (token.Parent is AnonymousObjectCreationExpressionSyntax { Initializers.Count: > 0 } anonymous
            && token == anonymous.CloseBraceToken)
        {
            return anonymous;
        }

        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            if (node.SpanStart != token.SpanStart)
                return null;
            var owner = node.Parent;
            var elements = owner switch
                {
                    InitializerExpressionSyntax candidate => (IReadOnlyList<SyntaxNode>)candidate.Expressions,
                    AnonymousObjectCreationExpressionSyntax candidate => candidate.Initializers,
                    _ => null
                };
            if (elements is not null)
                return elements.Contains(node) ? owner : null;
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
