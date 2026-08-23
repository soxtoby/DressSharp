using System.Collections.Immutable;
using DressSharp.Architecture;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

sealed class RuleCatalog
{
    const int CurrentVersion = 1;

    RuleCatalog()
    {
        FileRules =
            [
                new NamespaceStyleRule()
            ];
        UsingRules =
            [
                new UsingOrderRule(RuleKey.DressGlobalUsingOrder),
                new UsingOrderRule(RuleKey.DressUsingKindOrder),
                new SystemUsingSortRule()
            ];
        MemberRules =
            [
                new MemberBodyRule(RuleKey.DressMethodBody, MemberBodyKind.Method),
                new MemberBodyRule(RuleKey.DressConstructorBody, MemberBodyKind.Constructor),
                new MemberBodyRule(RuleKey.DressOperatorBody, MemberBodyKind.Operator),
                new MemberBodyRule(RuleKey.DressPropertyBody, MemberBodyKind.Property),
                new MemberBodyRule(RuleKey.DressIndexerBody, MemberBodyKind.Indexer),
                new MemberBodyRule(RuleKey.DressAccessorBody, MemberBodyKind.Accessor),
                new LambdaBodyRule(),
                new ConditionalBracesRule(),
                new ModifierOrderRule()
            ];
        SpacingRules =
            [
                new CastSpacingRule(),
                new ControlFlowKeywordSpacingRule(),
                new ParenthesisSpacingRule(),
                new BaseListColonSpacingRule(SpacingSide.Before),
                new BaseListColonSpacingRule(SpacingSide.After),
                new BinaryOperatorSpacingRule(),
                new MethodDeclarationSpacingRule(ParenthesisSpacingKind.Contents),
                new MethodDeclarationSpacingRule(ParenthesisSpacingKind.EmptyContents),
                new MethodDeclarationSpacingRule(ParenthesisSpacingKind.BeforeOpening),
                new MethodCallSpacingRule(ParenthesisSpacingKind.Contents),
                new MethodCallSpacingRule(ParenthesisSpacingKind.EmptyContents),
                new MethodCallSpacingRule(ParenthesisSpacingKind.BeforeOpening),
                new CommaSpacingRule(SpacingSide.After),
                new CommaSpacingRule(SpacingSide.Before),
                new DotSpacingRule(SpacingSide.After),
                new DotSpacingRule(SpacingSide.Before),
                new ForSemicolonSpacingRule(SpacingSide.After),
                new ForSemicolonSpacingRule(SpacingSide.Before),
                new DeclarationSpacingRule(),
                new BracketSpacingRule(BracketSpacingKind.BeforeOpening),
                new BracketSpacingRule(BracketSpacingKind.Contents)
            ];
        NewLineRules =
            [
                new NewLineRule(RuleKey.CSharpNewLineBeforeOpenBrace, NewLineKind.OpenBrace, ["all", "none", "accessors", "anonymous_methods", "anonymous_types", "control_blocks", "events", "indexers", "lambdas", "local_functions", "methods", "object_collection_array_initializers", "properties", "types"]),
                new NewLineRule(RuleKey.CSharpNewLineBeforeElse, NewLineKind.Else, ["true", "false"]),
                new NewLineRule(RuleKey.CSharpNewLineBeforeCatch, NewLineKind.Catch, ["true", "false"]),
                new NewLineRule(RuleKey.CSharpNewLineBeforeFinally, NewLineKind.Finally, ["true", "false"]),
                new NewLineRule(RuleKey.CSharpNewLineBeforeMembersInObjectInitializers, NewLineKind.ObjectInitializerMembers, ["true", "false"]),
                new NewLineRule(RuleKey.CSharpNewLineBeforeMembersInAnonymousTypes, NewLineKind.AnonymousTypeMembers, ["true", "false"]),
                new NewLineRule(RuleKey.CSharpNewLineBetweenQueryExpressionClauses, NewLineKind.QueryClauses, ["true", "false"])
            ];

        var allRules = FileRules.Cast<IFormattingRule>()
            .Concat(UsingRules)
            .Concat(MemberRules)
            .Concat(SpacingRules)
            .Concat(NewLineRules)
            .ToImmutableArray();
        Validate(allRules);
    }

    internal int Version { get; } = CurrentVersion;
    internal ImmutableArray<ISyntaxFormattingRule> FileRules { get; }
    internal ImmutableArray<IUsingFormattingRule> UsingRules { get; }
    internal ImmutableArray<ISyntaxFormattingRule> MemberRules { get; }
    internal ImmutableArray<TokenSpacingRule> SpacingRules { get; }
    internal ImmutableArray<NewLineRule> NewLineRules { get; }

    internal static RuleCatalog BuiltIn { get; } = new();

    static void Validate(ImmutableArray<IFormattingRule> rules)
    {
        if (rules.Any(rule => rule.Metadata.AcceptedValues.IsDefaultOrEmpty ||
            string.IsNullOrWhiteSpace(rule.Metadata.OwnedSyntax) ||
            string.IsNullOrWhiteSpace(rule.Metadata.Invariant)))
        {
            throw new ArgumentException("Every rule requires complete catalog metadata.", nameof(rules));
        }

        if (rules.Select(rule => rule.Metadata.RuleKey).Distinct().Count() != rules.Length)
            throw new ArgumentException("Rule preference keys must be unique.", nameof(rules));
        if (rules.Any(rule => rule.Metadata.AcceptedValues.Distinct(StringComparer.OrdinalIgnoreCase).Count() != rule.Metadata.AcceptedValues.Length))
            throw new ArgumentException("Rule accepted values must be unique.", nameof(rules));
    }
}

sealed record RuleMetadata(
    RuleKey RuleKey,
    ImmutableArray<string> AcceptedValues,
    string OwnedSyntax,
    string Invariant);

interface IFormattingRule
{
    RuleMetadata Metadata { get; }

    /// <summary>
    /// The syntax kinds this rule can transform. A file containing none of them cannot be changed by
    /// the rule, so it need not be run over it. An empty set means the rule may always apply.
    /// </summary>
    /// <remarks>
    /// Most rules cost the same to run over a file that has nothing for them as over one that does,
    /// because finding their targets means walking the whole tree. Declaring what they look for lets
    /// that walk be shared and the rule skipped outright.
    /// </remarks>
    ImmutableArray<SyntaxKind> TargetKinds => [];
}

interface ISyntaxFormattingRule : IFormattingRule
{
    SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context);
}

interface IUsingFormattingRule : IFormattingRule
{
    SyntaxList<UsingDirectiveSyntax> Rewrite(SyntaxList<UsingDirectiveSyntax> source, string preference, RuleContext context);
}
