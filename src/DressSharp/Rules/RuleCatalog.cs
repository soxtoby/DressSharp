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
        NamespaceStyleRule[] fileRules =
        [
            new NamespaceStyleRule()
        ];
        IUsingFormattingRule[] usingRules =
        [
                new UsingOrderRule(RuleKey.DressGlobalUsingOrder, RuleValues.Choice("first", "last", "mixed"), "first"),
                new UsingOrderRule(RuleKey.DressUsingKindOrder, RuleValues.Permutation("ordinary", "static", "alias"), "ordinary,static,alias"),
            new SystemUsingSortRule(),
            new ImportGroupSeparationRule()
        ];
        ISyntaxFormattingRule[] memberRules =
        [
                new MemberBodyRule(RuleKey.DressMethodBody, MemberBodyKind.Method, "expression"),
                new MemberBodyRule(RuleKey.DressConstructorBody, MemberBodyKind.Constructor, "expression"),
                new MemberBodyRule(RuleKey.DressOperatorBody, MemberBodyKind.Operator, "expression"),
                new MemberBodyRule(RuleKey.DressPropertyBody, MemberBodyKind.Property, "expression"),
                new MemberBodyRule(RuleKey.DressIndexerBody, MemberBodyKind.Indexer, "expression"),
                new MemberBodyRule(RuleKey.DressAccessorBody, MemberBodyKind.Accessor, "expression"),
            new LambdaBodyRule(),
            new ModifierOrderRule()
        ];
        IFormattingRule[] embeddedStatementRules =
        [
            new EmbeddedStatementPreferenceRule(
                    RuleKey.DressEmbeddedStatementPlacement,
                    ["same_line", "next_line"],
                    "next_line",
                "the boundary before brace-optional embedded statements",
                "Only boundary whitespace changes"),
            new EmbeddedStatementPreferenceRule(
                    RuleKey.DressEmbeddedStatementBraces,
                    ["compact", "balanced", "always"],
                    "balanced",
                "brace-optional embedded statements",
                "Brace changes preserve control flow and use planned body layout"),
            new EmbeddedStatementPreferenceRule(
                    RuleKey.DressBracesForMultilineStatementHeader,
                    ["true", "false"],
                    "true",
                "multiline statement headers owning brace-optional embedded statements",
                "A multiline statement header contributes only a brace constraint")
        ];
        TokenSpacingRule[] spacingRules =
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
        NewLineRule[] newLineRules =
        [
            new NewLineRule(RuleKey.CSharpNewLineBeforeOpenBrace,
                NewLineKind.OpenBrace,
                RuleValues.MultipleChoice([
                    "accessors", "anonymous_methods", "anonymous_types", "control_blocks", "events", "indexers", "lambdas", "local_functions", "methods",
                    "object_collection_array_initializers", "properties", "types"
                ], "all", "none"), "all"),
            new NewLineRule(RuleKey.CSharpNewLineBeforeElse, NewLineKind.Else, RuleValues.Boolean(), "true"),
            new NewLineRule(RuleKey.CSharpNewLineBeforeCatch, NewLineKind.Catch, RuleValues.Boolean(), "true"),
            new NewLineRule(RuleKey.CSharpNewLineBeforeFinally, NewLineKind.Finally, RuleValues.Boolean(), "true"),
            new NewLineRule(RuleKey.CSharpNewLineBeforeMembersInObjectInitializers, NewLineKind.ObjectInitializerMembers, RuleValues.Boolean(), "true"),
            new NewLineRule(RuleKey.CSharpNewLineBeforeMembersInAnonymousTypes, NewLineKind.AnonymousTypeMembers, RuleValues.Boolean(), "true"),
            new NewLineRule(RuleKey.CSharpNewLineBetweenQueryExpressionClauses, NewLineKind.QueryClauses, RuleValues.Boolean(), "true")
        ];
        IndentationRule[] indentationRules =
        [
            new IndentationRule(RuleKey.CSharpIndentSwitchLabels, ["true", "false"], "true"),
            new IndentationRule(RuleKey.CSharpIndentCaseContents, ["true", "false"], "true"),
            new IndentationRule(RuleKey.CSharpIndentLabels, ["flush_left", "no_change", "one_less_than_current"], "one_less_than_current"),
            new IndentationRule(RuleKey.CSharpIndentBlockContents, ["true", "false"], "true"),
            new IndentationRule(RuleKey.CSharpIndentBraces, ["true", "false"], "false"),
            new IndentationRule(RuleKey.CSharpIndentCaseContentsWhenBlock, ["true", "false"], "true")
        ];
        SingleLinePreservationRule[] preservationRules =
        [
            new SingleLinePreservationRule(SingleLinePreservationKind.Blocks),
            new SingleLinePreservationRule(SingleLinePreservationKind.Statements)
        ];
        InitializerIndentationRule[] initializerIndentationRules =
        [
            new InitializerIndentationRule(RuleKey.DressObjectInitializerIndentation, InitializerKind.Object, "indented"),
            new InitializerIndentationRule(RuleKey.DressCollectionInitializerIndentation, InitializerKind.Collection, "indented"),
            new InitializerIndentationRule(RuleKey.DressArrayInitializerIndentation, InitializerKind.Array, "indented"),
            new InitializerIndentationRule(RuleKey.DressWithInitializerIndentation, InitializerKind.With, "indented"),
            new InitializerIndentationRule(RuleKey.DressCollectionExpressionIndentation, InitializerKind.CollectionExpression, "not_indented")
        ];
        SyntaxWrappingRule[] syntaxWrappingRules =
        [
            new SyntaxWrappingRule(RuleKey.DressArgumentsLayout, SyntaxWrappingKind.Arguments, "auto"),
            new SyntaxWrappingRule(RuleKey.DressParametersLayout, SyntaxWrappingKind.Parameters, "auto"),
            new SyntaxWrappingRule(RuleKey.DressInitializersLayout, SyntaxWrappingKind.Initializers, "auto"),
            new SyntaxWrappingRule(RuleKey.DressCollectionExpressionsLayout, SyntaxWrappingKind.CollectionExpressions, "auto"),
            new SyntaxWrappingRule(RuleKey.DressBaseTypeListsLayout, SyntaxWrappingKind.BaseTypeLists, "auto"),
            new SyntaxWrappingRule(RuleKey.DressConstraintClausesLayout, SyntaxWrappingKind.ConstraintClauses, "auto"),
            new SyntaxWrappingRule(RuleKey.DressMemberAccessChainsLayout, SyntaxWrappingKind.MemberAccessChains, "auto"),
            new SyntaxWrappingRule(RuleKey.DressBinaryExpressionsLayout, SyntaxWrappingKind.BinaryExpressions, "auto"),
            new SyntaxWrappingRule(RuleKey.DressConditionalExpressionsLayout, SyntaxWrappingKind.ConditionalExpressions, "auto"),
            new SyntaxWrappingRule(RuleKey.DressQueryClausesLayout, SyntaxWrappingKind.QueryClauses, "auto"),
            new SyntaxWrappingRule(RuleKey.DressAttributesLayout, SyntaxWrappingKind.Attributes, "auto")
        ];
        BlankLineRule[] blankLineRules =
        [
            new BlankLineRule(RuleKey.DressBlankLinesAroundNamespaces, BlankLineKind.AroundNamespaces, "1"),
            new BlankLineRule(RuleKey.DressBlankLinesAroundTypes, BlankLineKind.AroundTypes, "1"),
            new BlankLineRule(RuleKey.DressBlankLinesBetweenMembers, BlankLineKind.BetweenMembers, "1"),
            new BlankLineRule(RuleKey.DressBlankLinesBetweenUsingGroups, BlankLineKind.BetweenUsingGroups, "1"),
            new BlankLineRule(RuleKey.DressBlankLinesBetweenMemberCategories, BlankLineKind.BetweenMemberCategories, "1"),
            new BlankLineRule(RuleKey.DressMaxConsecutiveBlankLines, BlankLineKind.Maximum, "1")
        ];
        CommentRule[] commentRules =
        [
            new CommentRule(RuleKey.DressLineCommentSpacing, CommentKind.LineSpacing, ["none", "single"], "single"),
            new CommentRule(RuleKey.DressBlockCommentSpacing, CommentKind.BlockSpacing, ["none", "single"], "single"),
            new CommentRule(RuleKey.DressAttachedCommentPlacement, CommentKind.AttachedPlacement, ["same_line", "own_line", "auto"], "auto"),
            new CommentRule(RuleKey.DressXmlCommentPlacement, CommentKind.XmlPlacement, ["attached", "separated"], "attached"),
            new CommentRule(RuleKey.DressXmlElementLayout, CommentKind.XmlElementLayout, ["single_line", "multi_line"], "single_line")
        ];

        var formattingRules = fileRules.Cast<IFormattingRule>()
            .Concat(usingRules)
            .Concat(blankLineRules)
            .Concat(commentRules)
            .Concat(memberRules)
            .Concat(embeddedStatementRules)
            .Concat(initializerIndentationRules)
            .Concat(spacingRules)
            .Concat(preservationRules)
            .Concat(syntaxWrappingRules)
            .Concat(newLineRules)
            .Concat(indentationRules)
            .Append(new LambdaBlockIndentationRule())
            .ToImmutableArray();
        IRule[] metadataOnlyRules =
        [
            Metadata(RuleKey.Charset, "Charset", "File", "Selects the encoding used when DressSharp writes the file.",
                RuleValues.Choice("latin1", "utf-8", "utf-8-bom", "utf-16be", "utf-16le"), "utf-8"),
            Metadata(RuleKey.EndOfLine, "End of line", "File", "Selects the line-ending sequence used when DressSharp writes the file.",
                RuleValues.Choice("cr", "lf", "crlf"), "lf"),
            Metadata(RuleKey.InsertFinalNewline, "Insert final newline", "File", "Controls whether the written file ends with a newline.",
                RuleValues.Boolean(), "true"),
            Metadata(RuleKey.TrimTrailingWhitespace, "Trim trailing whitespace", "File", "Controls whether trailing line whitespace is removed.",
                RuleValues.Boolean(), "true"),
            Metadata(RuleKey.IndentStyle, "Indent style", "Indentation", "Selects tabs or spaces for indentation written by layout rules.",
                RuleValues.Choice("space", "tab"), "space"),
            Metadata(RuleKey.IndentSize, "Indent size", "Indentation", "Sets indentation width, or follows tab_width when set to tab.",
                RuleValues.Integer(1, "tab"), "4"),
            Metadata(RuleKey.TabWidth, "Tab width", "Indentation", "Sets the display width used for tab indentation.",
                RuleValues.Integer(1), "4"),
            Metadata(RuleKey.MaxLineLength, "Maximum line length", "Wrapping", "Sets the target line width used by auto layout preferences.",
                RuleValues.Integer(1, "off"), "180")
        ];
        var rulesByKey = metadataOnlyRules.Concat(formattingRules)
            .ToDictionary(rule => rule.Metadata.RuleKey);
        Rules = Enum.GetValues<RuleKey>().Select(key => rulesByKey[key]).ToImmutableArray();
        Validate(Rules);

        FileRules = Rules.OfType<NamespaceStyleRule>().Cast<ISyntaxFormattingRule>().ToImmutableArray();
        UsingRules = Rules.OfType<IUsingFormattingRule>().ToImmutableArray();
        MemberRules = Rules.Where(rule => rule is MemberBodyRule or LambdaBodyRule or ModifierOrderRule)
            .Cast<ISyntaxFormattingRule>().ToImmutableArray();
        EmbeddedStatementRules = Rules.OfType<EmbeddedStatementPreferenceRule>().Cast<IFormattingRule>().ToImmutableArray();
        SpacingRules = Rules.OfType<TokenSpacingRule>().ToImmutableArray();
        NewLineRules = Rules.OfType<NewLineRule>().ToImmutableArray();
        IndentationRules = Rules.OfType<IndentationRule>().ToImmutableArray();
        PreservationRules = Rules.OfType<SingleLinePreservationRule>().ToImmutableArray();
        InitializerIndentationRules = Rules.OfType<InitializerIndentationRule>().ToImmutableArray();
        SyntaxWrappingRules = Rules.OfType<SyntaxWrappingRule>().ToImmutableArray();
        BlankLineRules = Rules.OfType<BlankLineRule>().ToImmutableArray();
        CommentRules = Rules.OfType<CommentRule>().ToImmutableArray();

        static IRule Metadata(
            RuleKey key,
            string name,
            string group,
            string description,
            RuleValueDefinition values,
            string defaultValue) => new MetadataRule(new()
            {
                RuleKey = key,
                Name = name,
                GroupName = group,
                Description = description,
                Values = values,
                DefaultValue = defaultValue,
                Example = """
                    class Example
                    {
                        string Text = "DressSharp";
                    }
                    """,
                OwnedSyntax = "file representation or shared layout settings",
                Invariant = "The configured value is applied by its owning formatter stage"
            });
    }

    internal int Version { get; } = CurrentVersion;
    internal ImmutableArray<IRule> Rules { get; }
    internal ImmutableArray<ISyntaxFormattingRule> FileRules { get; }
    internal ImmutableArray<IUsingFormattingRule> UsingRules { get; }
    internal ImmutableArray<ISyntaxFormattingRule> MemberRules { get; }
    internal ImmutableArray<IFormattingRule> EmbeddedStatementRules { get; }
    internal ImmutableArray<TokenSpacingRule> SpacingRules { get; }
    internal ImmutableArray<NewLineRule> NewLineRules { get; }
    internal ImmutableArray<IndentationRule> IndentationRules { get; }
    internal ImmutableArray<SingleLinePreservationRule> PreservationRules { get; }
    internal ImmutableArray<InitializerIndentationRule> InitializerIndentationRules { get; }
    internal ImmutableArray<SyntaxWrappingRule> SyntaxWrappingRules { get; }
    internal ImmutableArray<BlankLineRule> BlankLineRules { get; }
    internal ImmutableArray<CommentRule> CommentRules { get; }

    internal static RuleCatalog BuiltIn { get; } = new();

    static void Validate(ImmutableArray<IRule> rules)
    {
        if (rules.Any(rule => rule.Metadata.AcceptedValueForms.IsDefaultOrEmpty ||
            string.IsNullOrWhiteSpace(rule.Metadata.Name) ||
            string.IsNullOrWhiteSpace(rule.Metadata.GroupName) ||
            string.IsNullOrWhiteSpace(rule.Metadata.Description) ||
            string.IsNullOrWhiteSpace(rule.Metadata.DefaultValue) ||
            string.IsNullOrWhiteSpace(rule.Metadata.Example) ||
            string.IsNullOrWhiteSpace(rule.Metadata.OwnedSyntax) ||
            string.IsNullOrWhiteSpace(rule.Metadata.Invariant) ||
            !rule.Metadata.Accepts(rule.Metadata.DefaultValue)))
        {
            throw new ArgumentException("Every rule requires complete catalog metadata.", nameof(rules));
        }

        if (rules.Select(rule => rule.Metadata.RuleKey).Distinct().Count() != rules.Length)
            throw new ArgumentException("Rule preference keys must be unique.", nameof(rules));
        if (rules.Any(rule => rule.Metadata.AcceptedValueForms.Distinct(StringComparer.OrdinalIgnoreCase).Count() != rule.Metadata.AcceptedValueForms.Length))
            throw new ArgumentException("Rule accepted values must be unique.", nameof(rules));
        if (rules.Length != Enum.GetValues<RuleKey>().Length)
            throw new ArgumentException("Every supported preference requires exactly one rule.", nameof(rules));
    }
}

interface IRule
{
    RuleMetadata Metadata { get; }
}

interface IFormattingRule : IRule
{

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
