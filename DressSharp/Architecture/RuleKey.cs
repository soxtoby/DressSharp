using System.Collections.Frozen;

namespace DressSharp.Architecture;

/// <summary>
/// Every EditorConfig preference DressSharp knows. The snake_case spelling is the external surface
/// in <c>.editorconfig</c> files; this enum is how the rest of the program refers to a preference, so a
/// misspelled key is a compiler error rather than a silently disabled preference.
/// </summary>
enum RuleKey
{
    Charset,
    EndOfLine,
    InsertFinalNewline,
    TrimTrailingWhitespace,
    IndentStyle,
    IndentSize,
    TabWidth,
    MaxLineLength,
    CSharpNewLineBeforeOpenBrace,
    CSharpNewLineBeforeElse,
    CSharpNewLineBeforeCatch,
    CSharpNewLineBeforeFinally,
    CSharpNewLineBeforeMembersInObjectInitializers,
    CSharpNewLineBeforeMembersInAnonymousTypes,
    CSharpNewLineBetweenQueryExpressionClauses,
    CSharpIndentCaseContents,
    CSharpIndentSwitchLabels,
    CSharpIndentLabels,
    CSharpIndentBlockContents,
    CSharpIndentBraces,
    CSharpIndentCaseContentsWhenBlock,
    CSharpSpaceAfterCast,
    CSharpSpaceAfterKeywordsInControlFlowStatements,
    CSharpSpaceBetweenParentheses,
    CSharpSpaceBeforeColonInInheritanceClause,
    CSharpSpaceAfterColonInInheritanceClause,
    CSharpSpaceAroundBinaryOperators,
    CSharpSpaceBetweenMethodDeclarationParameterListParentheses,
    CSharpSpaceBetweenMethodDeclarationEmptyParameterListParentheses,
    CSharpSpaceBetweenMethodDeclarationNameAndOpenParenthesis,
    CSharpSpaceBetweenMethodCallParameterListParentheses,
    CSharpSpaceBetweenMethodCallEmptyParameterListParentheses,
    CSharpSpaceBetweenMethodCallNameAndOpeningParenthesis,
    CSharpSpaceAfterComma,
    CSharpSpaceBeforeComma,
    CSharpSpaceAfterDot,
    CSharpSpaceBeforeDot,
    CSharpSpaceAfterSemicolonInForStatement,
    CSharpSpaceBeforeSemicolonInForStatement,
    CSharpSpaceAroundDeclarationStatements,
    CSharpSpaceBeforeOpenSquareBrackets,
    CSharpSpaceBetweenEmptySquareBrackets,
    CSharpSpaceBetweenSquareBrackets,
    DressSpaceAfterCollectionSpreadOperator,
    DressSpaceAfterAttributeTargetColon,
    CSharpPreserveSingleLineBlocks,
    CSharpPreserveSingleLineStatements,
    DressPreserveTrivialSingleLineBlocks,
    DotnetSortSystemDirectivesFirst,
    DotnetSeparateImportDirectiveGroups,
    DressArgumentsLayout,
    DressParametersLayout,
    DressArgumentsClosingDelimiterPosition,
    DressParametersClosingDelimiterPosition,
    DressObjectInitializerClosingDelimiterPosition,
    DressCollectionInitializerClosingDelimiterPosition,
    DressArrayInitializerClosingDelimiterPosition,
    DressWithInitializerClosingDelimiterPosition,
    DressCollectionExpressionClosingDelimiterPosition,
    DressObjectInitializerLayout,
    DressCollectionInitializerLayout,
    DressArrayInitializerLayout,
    DressWithInitializerLayout,
    DressCollectionExpressionsLayout,
    DressBaseTypeListsLayout,
    DressConstraintClausesLayout,
    DressMemberAccessChainsLayout,
    DressBinaryExpressionsLayout,
    DressBinaryExpressionIndentation,
    DressConditionalExpressionsLayout,
    DressNestedConditionalStyle,
    DotnetStyleOperatorPlacementWhenWrapping,
    DressQueryClausesLayout,
    DressAttributesLayout,
    DressBlankLinesAroundNamespaces,
    DressBlankLinesAroundTypes,
    DressBlankLinesBetweenMembers,
    DressBlankLinesBetweenUsingGroups,
    DressBlankLinesBetweenMemberCategories,
    DressMaxConsecutiveBlankLines,
    DressLineCommentSpacing,
    DressBlockCommentSpacing,
    DressAttachedCommentPlacement,
    DressXmlCommentPlacement,
    DressXmlElementLayout,
    DressCommentAlign,
    DressGlobalUsingOrder,
    DressUsingKindOrder,
    CSharpPreferredModifierOrder,
    DressObjectInitializerIndentation,
    DressCollectionInitializerIndentation,
    DressArrayInitializerIndentation,
    DressWithInitializerIndentation,
    DressCollectionExpressionIndentation,
    DressCollectionExpressionArgumentIndentation,
    DressSwitchExpressionIndentation,
    DressLambdaBlockIndentation,
    DressMethodBody,
    DressConstructorBody,
    DressOperatorBody,
    DressPropertyBody,
    DressIndexerBody,
    DressAccessorBody,
    DressLambdaBody,
    DressNamespaceStyle,
    DressEmbeddedStatementPlacement,
    DressEmbeddedStatementBraces,
    DressBracesForMultilineStatementHeader,
    DressMultilineParameterListOpenBracePosition
}

static class RuleKeys
{
    static readonly FrozenDictionary<RuleKey, string> Names = Enum.GetValues<RuleKey>()
        .ToFrozenDictionary(key => key, ToSnakeCase);
    static readonly FrozenDictionary<string, RuleKey> ByName = Names
        .ToFrozenDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    internal static string ToName(this RuleKey key) => Names[key];

    internal static bool TryParse(string name, out RuleKey key) => ByName.TryGetValue(name, out key!);

    internal static RuleKey Parse(string name) => TryParse(name, out var key)
        ? key
        : throw new ArgumentException($"Unknown preference '{name}'.", nameof(name));

    /// <summary>
    /// The EditorConfig name for an enum member: snake case of its PascalCase name, keeping runs of
    /// capitals such as "CSharp" as one word.
    /// </summary>
    static string ToSnakeCase(RuleKey key)
    {
        var name = key.ToString();
        Span<char> buffer = stackalloc char[name.Length * 2];
        var length = 0;
        for (var index = 0; index < name.Length; index++)
        {
            var current = name[index];
            var startsWord = index > 0
                && char.IsUpper(current)
                && (char.IsLower(name[index - 1])
                || !TryGet(name, index + 1, out var next)
                || char.IsUpper(next)
                || index > 1 && char.IsUpper(name[index - 2]));
            if (startsWord)
                buffer[length++] = '_';
            buffer[length++] = char.ToLowerInvariant(current);
        }

        return new string(buffer[..length]);
    }

    static bool TryGet(string text, int index, out char value)
    {
        if (index < text.Length)
        {
            value = text[index];
            return true;
        }

        value = default;
        return false;
    }
}
