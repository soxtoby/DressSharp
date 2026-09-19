using System.Collections.Immutable;
using DressSharp.Architecture;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

sealed class RuleCatalog
{
    const int CurrentVersion = 4;

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
                new MemberBodyRule(RuleKey.DressMethodBody, "Method", "Body styles", MemberBodyKind.Method, "expression"),
                new MemberBodyRule(RuleKey.DressConstructorBody, "Constructor", "Body styles", MemberBodyKind.Constructor, "expression"),
                new MemberBodyRule(RuleKey.DressOperatorBody, "Operator", "Body styles", MemberBodyKind.Operator, "expression"),
                new MemberBodyRule(RuleKey.DressPropertyBody, "Property", "Body styles", MemberBodyKind.Property, "expression"),
                new MemberBodyRule(RuleKey.DressIndexerBody, "Indexer", "Body styles", MemberBodyKind.Indexer, "expression"),
                new MemberBodyRule(RuleKey.DressAccessorBody, "Accessor", "Body styles", MemberBodyKind.Accessor, "expression"),
            new LambdaBodyRule(),
            new ModifierOrderRule()
        ];
        IFormattingRule[] embeddedStatementRules =
        [
            new EmbeddedStatementPreferenceRule(
                    RuleKey.DressEmbeddedStatementPlacement, "Control statement body placement", "Embedded statements",
                    ["same_line", "next_line"],
                    "next_line",
                "the boundary before brace-optional embedded statements",
                "Only boundary whitespace changes",
                description: "Place the body of if, else, loops, using, lock, and fixed on the same line or a new line. "
                    + "For example: if (condition) return false;. Applies with or without braces. "
                    + "Embedded statement placement: single-line if, inline return, return new line.",
                expandedCaption: "Control statement body placement"),
            new EmbeddedStatementPreferenceRule(
                    RuleKey.DressEmbeddedStatementBraces, "Braces", "Embedded statements",
                    ["compact", "balanced", "always"],
                    "balanced",
                "brace-optional embedded statements",
                "Brace changes preserve control flow and use planned body layout"),
            new EmbeddedStatementPreferenceRule(
                    RuleKey.DressBracesForMultilineStatementHeader, "Braces for multiline statement header", "Embedded statements",
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
            new BaseListColonSpacingRule(RuleKey.CSharpSpaceBeforeColonInInheritanceClause, "Before colon in inheritance clause", null, SpacingSide.Before),
            new BaseListColonSpacingRule(RuleKey.CSharpSpaceAfterColonInInheritanceClause, "After colon in inheritance clause", null, SpacingSide.After),
            new BinaryOperatorSpacingRule(),
            new MethodDeclarationSpacingRule(RuleKey.CSharpSpaceBetweenMethodDeclarationParameterListParentheses, "Inside parentheses", "Method declarations",
                ParenthesisSpacingKind.Contents),
            new MethodDeclarationSpacingRule(RuleKey.CSharpSpaceBetweenMethodDeclarationEmptyParameterListParentheses, "Inside empty parentheses", "Method declarations",
                ParenthesisSpacingKind.EmptyContents),
            new MethodDeclarationSpacingRule(RuleKey.CSharpSpaceBetweenMethodDeclarationNameAndOpenParenthesis, "Before opening parenthesis", "Method declarations",
                ParenthesisSpacingKind.BeforeOpening),
            new MethodCallSpacingRule(RuleKey.CSharpSpaceBetweenMethodCallParameterListParentheses, "Inside parentheses", "Method calls", ParenthesisSpacingKind.Contents),
            new MethodCallSpacingRule(RuleKey.CSharpSpaceBetweenMethodCallEmptyParameterListParentheses, "Inside empty parentheses", "Method calls",
                ParenthesisSpacingKind.EmptyContents),
            new MethodCallSpacingRule(RuleKey.CSharpSpaceBetweenMethodCallNameAndOpeningParenthesis, "Before opening parenthesis", "Method calls",
                ParenthesisSpacingKind.BeforeOpening),
            new CommaSpacingRule(RuleKey.CSharpSpaceAfterComma, "After comma", null, SpacingSide.After),
            new CommaSpacingRule(RuleKey.CSharpSpaceBeforeComma, "Before comma", null, SpacingSide.Before),
            new DotSpacingRule(RuleKey.CSharpSpaceAfterDot, "After dot", null, SpacingSide.After),
            new DotSpacingRule(RuleKey.CSharpSpaceBeforeDot, "Before dot", null, SpacingSide.Before),
            new ForSemicolonSpacingRule(RuleKey.CSharpSpaceAfterSemicolonInForStatement, "After semicolon in for statement", null, SpacingSide.After),
            new ForSemicolonSpacingRule(RuleKey.CSharpSpaceBeforeSemicolonInForStatement, "Before semicolon in for statement", null, SpacingSide.Before),
            new DeclarationSpacingRule(),
            new BracketSpacingRule(RuleKey.CSharpSpaceBeforeOpenSquareBrackets, "Before open square brackets", null, BracketSpacingKind.BeforeOpening),
            new BracketSpacingRule(RuleKey.CSharpSpaceBetweenEmptySquareBrackets, "Between empty square brackets", null, BracketSpacingKind.EmptyContents),
            new BracketSpacingRule(RuleKey.CSharpSpaceBetweenSquareBrackets, "Between square brackets", null, BracketSpacingKind.Contents),
            new CollectionSpreadSpacingRule(),
            new AttributeTargetSpacingRule()
        ];
        NewLineRule[] newLineRules =
        [
            new NewLineRule(RuleKey.CSharpNewLineBeforeOpenBrace, "Before open brace", "Newlines",
                NewLineKind.OpenBrace,
                RuleValues.MultipleChoice([
                    "accessors", "anonymous_methods", "anonymous_types", "control_blocks", "events", "indexers", "lambdas", "local_functions", "methods",
                    "object_collection_array_initializers", "properties", "types"
                ], "all", "none"), "all"),
            new NewLineRule(RuleKey.CSharpNewLineBeforeElse, "Before else", "Newlines", NewLineKind.Else, RuleValues.Boolean(), "true"),
            new NewLineRule(RuleKey.CSharpNewLineBeforeCatch, "Before catch", "Newlines", NewLineKind.Catch, RuleValues.Boolean(), "true"),
            new NewLineRule(RuleKey.CSharpNewLineBeforeFinally, "Before finally", "Newlines", NewLineKind.Finally, RuleValues.Boolean(), "true"),
            new NewLineRule(RuleKey.CSharpNewLineBeforeMembersInObjectInitializers, "Before members in object initializers", "Newlines",
                NewLineKind.ObjectInitializerMembers, RuleValues.Boolean(), "true"),
            new NewLineRule(RuleKey.CSharpNewLineBeforeMembersInAnonymousTypes, "Before members in anonymous types", "Newlines",
                NewLineKind.AnonymousTypeMembers, RuleValues.Boolean(), "true"),
            new NewLineRule(RuleKey.CSharpNewLineBetweenQueryExpressionClauses, "Between query expression clauses", "Newlines",
                NewLineKind.QueryClauses, RuleValues.Boolean(), "true")
        ];
        IndentationRule[] indentationRules =
        [
            new IndentationRule(RuleKey.CSharpIndentSwitchLabels, "Switch labels", "Switch statements", ["true", "false"], "true"),
            new IndentationRule(RuleKey.CSharpIndentCaseContents, "Case contents", "Switch statements", ["true", "false"], "true"),
            new IndentationRule(RuleKey.CSharpIndentLabels, "Labels", null, ["flush_left", "no_change", "one_less_than_current"], "one_less_than_current"),
            new IndentationRule(RuleKey.CSharpIndentBlockContents, "Block contents", null, ["true", "false"], "true"),
            new IndentationRule(RuleKey.CSharpIndentBraces, "Braces", null, ["true", "false"], "false"),
            new IndentationRule(RuleKey.CSharpIndentCaseContentsWhenBlock, "Case contents when block", "Switch statements", ["true", "false"], "true")
        ];
        SingleLinePreservationRule[] preservationRules =
        [
            new SingleLinePreservationRule(RuleKey.CSharpPreserveSingleLineBlocks, "Blocks", "Preserve single line", SingleLinePreservationKind.Blocks),
            new SingleLinePreservationRule(RuleKey.CSharpPreserveSingleLineStatements, "Statements", "Preserve single line", SingleLinePreservationKind.Statements),
            new SingleLinePreservationRule(RuleKey.DressPreserveTrivialSingleLineBlocks, "Trivial blocks", "Preserve single line", SingleLinePreservationKind.TrivialBlocks)
        ];
        InitializerIndentationRule[] initializerIndentationRules =
        [
            new InitializerIndentationRule(RuleKey.DressObjectInitializerIndentation, "Object initializer", "Initializers and collections", InitializerKind.Object, "indented"),
            new InitializerIndentationRule(RuleKey.DressCollectionInitializerIndentation, "Collection initializer", "Initializers and collections",
                InitializerKind.Collection, "indented"),
            new InitializerIndentationRule(RuleKey.DressArrayInitializerIndentation, "Array initializer", "Initializers and collections", InitializerKind.Array, "indented"),
            new InitializerIndentationRule(RuleKey.DressWithInitializerIndentation, "With initializer", "Initializers and collections", InitializerKind.With, "indented"),
            new InitializerIndentationRule(RuleKey.DressCollectionExpressionIndentation, "Collection expression outside arguments", "Initializers and collections",
                InitializerKind.CollectionExpression, "indented"),
            new InitializerIndentationRule(RuleKey.DressCollectionExpressionArgumentIndentation, "Collection expression argument", "Initializers and collections",
                InitializerKind.CollectionExpressionArgument, "not_indented")
        ];
        SyntaxWrappingRule[] syntaxWrappingRules =
        [
            new SyntaxWrappingRule(RuleKey.DressArgumentsLayout, "Arguments", "Lists", SyntaxWrappingKind.Arguments, "auto"),
            new SyntaxWrappingRule(RuleKey.DressParametersLayout, "Parameters", "Lists", SyntaxWrappingKind.Parameters, "auto"),
            new SyntaxWrappingRule(RuleKey.DressObjectInitializerLayout, "Object initializer", "Initializers", SyntaxWrappingKind.ObjectInitializers, "auto"),
            new SyntaxWrappingRule(RuleKey.DressCollectionInitializerLayout, "Collection initializer", "Initializers", SyntaxWrappingKind.CollectionInitializers, "auto"),
            new SyntaxWrappingRule(RuleKey.DressArrayInitializerLayout, "Array initializer", "Initializers", SyntaxWrappingKind.ArrayInitializers, "auto"),
            new SyntaxWrappingRule(RuleKey.DressWithInitializerLayout, "With initializer", "Initializers", SyntaxWrappingKind.WithInitializers, "auto"),
            new SyntaxWrappingRule(RuleKey.DressCollectionExpressionsLayout, "Collection expressions", "Lists", SyntaxWrappingKind.CollectionExpressions, "auto"),
            new SyntaxWrappingRule(RuleKey.DressBaseTypeListsLayout, "Base type lists", "Lists", SyntaxWrappingKind.BaseTypeLists, "auto"),
            new SyntaxWrappingRule(RuleKey.DressConstraintClausesLayout, "Constraint clauses", "Lists", SyntaxWrappingKind.ConstraintClauses, "auto"),
            new SyntaxWrappingRule(RuleKey.DressMemberAccessChainsLayout, "Member access chains", "Expressions", SyntaxWrappingKind.MemberAccessChains, "auto"),
            new SyntaxWrappingRule(RuleKey.DressBinaryExpressionsLayout, "Binary expressions", "Expressions", SyntaxWrappingKind.BinaryExpressions, "auto"),
            new SyntaxWrappingRule(RuleKey.DressConditionalExpressionsLayout, "Conditional expressions", "Expressions", SyntaxWrappingKind.ConditionalExpressions, "auto"),
            new SyntaxWrappingRule(RuleKey.DressQueryClausesLayout, "Query clauses", "Expressions", SyntaxWrappingKind.QueryClauses, "auto"),
            new SyntaxWrappingRule(RuleKey.DressAttributesLayout, "Attributes", "Lists", SyntaxWrappingKind.Attributes, "auto")
        ];
        BlankLineRule[] blankLineRules =
        [
            new BlankLineRule(RuleKey.DressBlankLinesAroundNamespaces, "Around namespaces", null, BlankLineKind.AroundNamespaces, "1"),
            new BlankLineRule(RuleKey.DressBlankLinesAroundTypes, "Around types", null, BlankLineKind.AroundTypes, "1"),
            new BlankLineRule(RuleKey.DressBlankLinesBetweenMembers, "Between members", null, BlankLineKind.BetweenMembers, "1"),
            new BlankLineRule(RuleKey.DressBlankLinesBetweenUsingGroups, "Between using groups", null, BlankLineKind.BetweenUsingGroups, "1"),
            new BlankLineRule(RuleKey.DressBlankLinesBetweenMemberCategories, "Between member categories", null, BlankLineKind.BetweenMemberCategories, "1"),
            new BlankLineRule(RuleKey.DressMaxConsecutiveBlankLines, "Max consecutive blank lines", null, BlankLineKind.Maximum, "1")
        ];
        CommentRule[] commentRules =
        [
            new CommentRule(RuleKey.DressLineCommentSpacing, "Spacing", "Line comments", CommentKind.LineSpacing, ["none", "single"], "single"),
            new CommentRule(RuleKey.DressBlockCommentSpacing, "Spacing", "Block comments", CommentKind.BlockSpacing, ["none", "single"], "single"),
            new CommentRule(RuleKey.DressAttachedCommentPlacement, "Attached comment placement", null, CommentKind.AttachedPlacement, ["same_line", "own_line", "auto"], "auto"),
            new CommentRule(RuleKey.DressXmlCommentPlacement, "Placement", "XML documentation", CommentKind.XmlPlacement, ["attached", "separated"], "attached"),
            new CommentRule(RuleKey.DressXmlElementLayout, "Element layout", "XML documentation", CommentKind.XmlElementLayout, ["single_line", "multi_line"], "single_line")
        ];

        var formattingRules = fileRules.Cast<IFormattingRule>()
            .Concat(usingRules)
            .Concat(blankLineRules)
            .Concat(commentRules)
            .Append(new CommentAlignmentRule())
            .Concat(memberRules)
            .Concat(embeddedStatementRules)
            .Concat(initializerIndentationRules)
            .Concat(spacingRules)
            .Concat(preservationRules)
            .Concat(syntaxWrappingRules)
            .Append(new BinaryExpressionIndentationRule())
            .Append(new NestedConditionalStyleRule())
            .Append(new OperatorPlacementRule())
            .Concat(ClosingDelimiterPositionRule.All())
            .Append(new MultilineParameterListOpenBracePositionRule())
            .Concat(newLineRules)
            .Concat(indentationRules)
            .Append(new SwitchExpressionIndentationRule())
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
            Metadata(RuleKey.IndentStyle, "Style", "Indentation", "Selects tabs or spaces for indentation written by layout rules.",
                RuleValues.Choice("space", "tab"), "space", expandedCaption: "Indent style"),
            Metadata(RuleKey.IndentSize, "Size", "Indentation", "Sets indentation width, or follows tab_width when set to tab.",
                RuleValues.Integer(1, "tab"), "4", expandedCaption: "Indent size"),
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
            string caption,
            string group,
            string description,
            RuleValueDefinition values,
            string defaultValue,
            string? expandedCaption = null) => new MetadataRule(new()
            {
                RuleKey = key,
                Caption = caption,
                ExpandedCaption = expandedCaption ?? caption,
                GroupName = group,
                Description = description,
                Values = values,
                DefaultValue = defaultValue,
                ExamplePreferences = key switch
                {
                    RuleKey.IndentStyle => ImmutableDictionary<RuleKey, string>.Empty.Add(RuleKey.CSharpIndentBlockContents, "true"),
                    RuleKey.IndentSize => ImmutableDictionary<RuleKey, string>.Empty.Add(RuleKey.CSharpIndentBlockContents, "true").Add(RuleKey.IndentStyle, "space"),
                    RuleKey.TabWidth => ImmutableDictionary<RuleKey, string>.Empty.Add(RuleKey.CSharpIndentBlockContents, "true").Add(RuleKey.IndentStyle, "space").Add(RuleKey.IndentSize, "tab"),
                    RuleKey.MaxLineLength => ImmutableDictionary<RuleKey, string>.Empty.Add(RuleKey.DressArgumentsLayout, "auto"),
                    _ => ImmutableDictionary<RuleKey, string>.Empty
                },
                Example = key switch
                {
                    RuleKey.TrimTrailingWhitespace => "class Example { }   \n",
                    RuleKey.MaxLineLength => "Call(firstArgument, secondArgument, thirdArgument);",
                    _ => """
                    class Example
                    {
                        string Text = "DressSharp";
                    }
                    """
                },
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
            string.IsNullOrWhiteSpace(rule.Metadata.ExpandedCaption) ||
            string.IsNullOrWhiteSpace(rule.Metadata.Caption) ||
            (rule.Metadata.SubgroupName is not null && string.IsNullOrWhiteSpace(rule.Metadata.SubgroupName)) ||
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
