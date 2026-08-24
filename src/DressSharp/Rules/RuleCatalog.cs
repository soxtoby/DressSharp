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
                new SystemUsingSortRule(),
                new ImportGroupSeparationRule()
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
                new BracketSpacingRule(BracketSpacingKind.EmptyContents),
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
        IndentationRules =
            [
                new IndentationRule(RuleKey.CSharpIndentSwitchLabels, ["true", "false"]),
                new IndentationRule(RuleKey.CSharpIndentCaseContents, ["true", "false"]),
                new IndentationRule(RuleKey.CSharpIndentLabels, ["flush_left", "no_change", "one_less_than_current"]),
                new IndentationRule(RuleKey.CSharpIndentBlockContents, ["true", "false"]),
                new IndentationRule(RuleKey.CSharpIndentBraces, ["true", "false"]),
                new IndentationRule(RuleKey.CSharpIndentCaseContentsWhenBlock, ["true", "false"])
            ];
        PreservationRules =
            [
                new SingleLinePreservationRule(SingleLinePreservationKind.Blocks),
                new SingleLinePreservationRule(SingleLinePreservationKind.Statements)
            ];
        InitializerIndentationRules =
            [
                new InitializerIndentationRule(RuleKey.DressObjectInitializerIndentation, InitializerKind.Object),
                new InitializerIndentationRule(RuleKey.DressCollectionInitializerIndentation, InitializerKind.Collection),
                new InitializerIndentationRule(RuleKey.DressArrayInitializerIndentation, InitializerKind.Array),
                new InitializerIndentationRule(RuleKey.DressWithInitializerIndentation, InitializerKind.With),
                new InitializerIndentationRule(RuleKey.DressCollectionExpressionIndentation, InitializerKind.CollectionExpression)
            ];
        ConstructLayoutRules =
            [
                new ConstructLayoutRule(RuleKey.DressArgumentsLayout, ConstructLayoutKind.Arguments),
                new ConstructLayoutRule(RuleKey.DressParametersLayout, ConstructLayoutKind.Parameters),
                new ConstructLayoutRule(RuleKey.DressInitializersLayout, ConstructLayoutKind.Initializers),
                new ConstructLayoutRule(RuleKey.DressCollectionExpressionsLayout, ConstructLayoutKind.CollectionExpressions),
                new ConstructLayoutRule(RuleKey.DressBaseTypeListsLayout, ConstructLayoutKind.BaseTypeLists),
                new ConstructLayoutRule(RuleKey.DressConstraintClausesLayout, ConstructLayoutKind.ConstraintClauses),
                new ConstructLayoutRule(RuleKey.DressMemberAccessChainsLayout, ConstructLayoutKind.MemberAccessChains),
                new ConstructLayoutRule(RuleKey.DressBinaryExpressionsLayout, ConstructLayoutKind.BinaryExpressions),
                new ConstructLayoutRule(RuleKey.DressConditionalExpressionsLayout, ConstructLayoutKind.ConditionalExpressions),
                new ConstructLayoutRule(RuleKey.DressQueryClausesLayout, ConstructLayoutKind.QueryClauses),
                new ConstructLayoutRule(RuleKey.DressAttributesLayout, ConstructLayoutKind.Attributes)
            ];
        BlankLineRules =
            [
                new BlankLineRule(RuleKey.DressBlankLinesAroundNamespaces, BlankLineKind.AroundNamespaces),
                new BlankLineRule(RuleKey.DressBlankLinesAroundTypes, BlankLineKind.AroundTypes),
                new BlankLineRule(RuleKey.DressBlankLinesBetweenMembers, BlankLineKind.BetweenMembers),
                new BlankLineRule(RuleKey.DressBlankLinesBetweenUsingGroups, BlankLineKind.BetweenUsingGroups),
                new BlankLineRule(RuleKey.DressBlankLinesBetweenMemberCategories, BlankLineKind.BetweenMemberCategories),
                new BlankLineRule(RuleKey.DressMaxConsecutiveBlankLines, BlankLineKind.Maximum)
            ];
        CommentRules =
            [
                new CommentRule(RuleKey.DressLineCommentSpacing, CommentKind.LineSpacing, ["none", "single"]),
                new CommentRule(RuleKey.DressBlockCommentSpacing, CommentKind.BlockSpacing, ["none", "single"]),
                new CommentRule(RuleKey.DressAttachedCommentPlacement, CommentKind.AttachedPlacement, ["same_line", "own_line", "auto"]),
                new CommentRule(RuleKey.DressXmlCommentPlacement, CommentKind.XmlPlacement, ["attached", "separated"]),
                new CommentRule(RuleKey.DressXmlElementLayout, CommentKind.XmlElementLayout, ["single_line", "multi_line"])
            ];

        Rules = FileRules.Cast<IFormattingRule>()
            .Concat(UsingRules)
            .Concat(BlankLineRules)
            .Concat(CommentRules)
            .Concat(MemberRules)
            .Concat(InitializerIndentationRules)
            .Concat(SpacingRules)
            .Concat(PreservationRules)
            .Concat(ConstructLayoutRules)
            .Concat(NewLineRules)
            .Concat(IndentationRules)
            .ToImmutableArray();
        Validate(Rules);
    }

    internal int Version { get; } = CurrentVersion;
    internal ImmutableArray<IFormattingRule> Rules { get; }
    internal ImmutableArray<ISyntaxFormattingRule> FileRules { get; }
    internal ImmutableArray<IUsingFormattingRule> UsingRules { get; }
    internal ImmutableArray<ISyntaxFormattingRule> MemberRules { get; }
    internal ImmutableArray<TokenSpacingRule> SpacingRules { get; }
    internal ImmutableArray<NewLineRule> NewLineRules { get; }
    internal ImmutableArray<IndentationRule> IndentationRules { get; }
    internal ImmutableArray<SingleLinePreservationRule> PreservationRules { get; }
    internal ImmutableArray<InitializerIndentationRule> InitializerIndentationRules { get; }
    internal ImmutableArray<ConstructLayoutRule> ConstructLayoutRules { get; }
    internal ImmutableArray<BlankLineRule> BlankLineRules { get; }
    internal ImmutableArray<CommentRule> CommentRules { get; }

    internal static RuleCatalog BuiltIn { get; } = new();

    static void Validate(ImmutableArray<IFormattingRule> rules)
    {
        if (rules.Any(rule => rule.Metadata.AcceptedValueForms.IsDefaultOrEmpty ||
            string.IsNullOrWhiteSpace(rule.Metadata.OwnedSyntax) ||
            string.IsNullOrWhiteSpace(rule.Metadata.Invariant)))
        {
            throw new ArgumentException("Every rule requires complete catalog metadata.", nameof(rules));
        }

        if (rules.Select(rule => rule.Metadata.RuleKey).Distinct().Count() != rules.Length)
            throw new ArgumentException("Rule preference keys must be unique.", nameof(rules));
        if (rules.Any(rule => rule.Metadata.AcceptedValueForms.Distinct(StringComparer.OrdinalIgnoreCase).Count() != rule.Metadata.AcceptedValueForms.Length))
            throw new ArgumentException("Rule accepted values must be unique.", nameof(rules));
    }
}

enum RuleSafetyClass
{
    Layout,
    SyntaxTransformation
}

sealed record RuleMetadata(
    RuleKey RuleKey,
    ImmutableArray<string> AcceptedValueForms,
    string OwnedSyntax,
    string Invariant)
{
    internal Func<string, bool>? ValueValidator { get; init; }

    internal bool Accepts(string value) => ValueValidator?.Invoke(value)
        ?? AcceptedValueForms.Contains(value, StringComparer.OrdinalIgnoreCase);
}

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
