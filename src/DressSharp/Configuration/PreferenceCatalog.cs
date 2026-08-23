using DressSharp.Architecture;

namespace DressSharp.Configuration;

static class PreferenceCatalog
{
    static readonly HashSet<string> BooleanValues = new(StringComparer.OrdinalIgnoreCase) { "true", "false" };

    internal static IReadOnlyList<(RuleKey Key, string Default)> Defaults { get; } =
        [
            (RuleKey.Charset, "utf-8"),
            (RuleKey.EndOfLine, "lf"),
            (RuleKey.InsertFinalNewline, "true"),
            (RuleKey.TrimTrailingWhitespace, "true"),
            (RuleKey.IndentStyle, "space"),
            (RuleKey.IndentSize, "4"),
            (RuleKey.TabWidth, "4"),
            (RuleKey.MaxLineLength, "180"),
            (RuleKey.CSharpNewLineBeforeOpenBrace, "all"),
            (RuleKey.CSharpNewLineBeforeElse, "true"),
            (RuleKey.CSharpNewLineBeforeCatch, "true"),
            (RuleKey.CSharpNewLineBeforeFinally, "true"),
            (RuleKey.CSharpNewLineBeforeMembersInObjectInitializers, "true"),
            (RuleKey.CSharpNewLineBeforeMembersInAnonymousTypes, "true"),
            (RuleKey.CSharpNewLineBetweenQueryExpressionClauses, "true"),
            (RuleKey.CSharpIndentCaseContents, "true"),
            (RuleKey.CSharpIndentSwitchLabels, "true"),
            (RuleKey.CSharpIndentLabels, "one_less_than_current"),
            (RuleKey.CSharpIndentBlockContents, "true"),
            (RuleKey.CSharpIndentBraces, "false"),
            (RuleKey.CSharpIndentCaseContentsWhenBlock, "true"),
            (RuleKey.CSharpSpaceAfterCast, "false"),
            (RuleKey.CSharpSpaceAfterKeywordsInControlFlowStatements, "true"),
            (RuleKey.CSharpSpaceBetweenParentheses, "false"),
            (RuleKey.CSharpSpaceBeforeColonInInheritanceClause, "true"),
            (RuleKey.CSharpSpaceAfterColonInInheritanceClause, "true"),
            (RuleKey.CSharpSpaceAroundBinaryOperators, "before_and_after"),
            (RuleKey.CSharpSpaceBetweenMethodDeclarationParameterListParentheses, "false"),
            (RuleKey.CSharpSpaceBetweenMethodDeclarationEmptyParameterListParentheses, "false"),
            (RuleKey.CSharpSpaceBetweenMethodDeclarationNameAndOpenParenthesis, "false"),
            (RuleKey.CSharpSpaceBetweenMethodCallParameterListParentheses, "false"),
            (RuleKey.CSharpSpaceBetweenMethodCallEmptyParameterListParentheses, "false"),
            (RuleKey.CSharpSpaceBetweenMethodCallNameAndOpeningParenthesis, "false"),
            (RuleKey.CSharpSpaceAfterComma, "true"),
            (RuleKey.CSharpSpaceBeforeComma, "false"),
            (RuleKey.CSharpSpaceAfterDot, "false"),
            (RuleKey.CSharpSpaceBeforeDot, "false"),
            (RuleKey.CSharpSpaceAfterSemicolonInForStatement, "true"),
            (RuleKey.CSharpSpaceBeforeSemicolonInForStatement, "false"),
            (RuleKey.CSharpSpaceAroundDeclarationStatements, "false"),
            (RuleKey.CSharpSpaceBeforeOpenSquareBrackets, "false"),
            (RuleKey.CSharpSpaceBetweenEmptySquareBrackets, "false"),
            (RuleKey.CSharpSpaceBetweenSquareBrackets, "false"),
            (RuleKey.CSharpPreserveSingleLineBlocks, "true"),
            (RuleKey.CSharpPreserveSingleLineStatements, "true"),
            (RuleKey.DotnetSortSystemDirectivesFirst, "true"),
            (RuleKey.DotnetSeparateImportDirectiveGroups, "false"),
            (RuleKey.DressArgumentsLayout, "auto"),
            (RuleKey.DressParametersLayout, "auto"),
            (RuleKey.DressInitializersLayout, "auto"),
            (RuleKey.DressCollectionExpressionsLayout, "auto"),
            (RuleKey.DressBaseTypeListsLayout, "auto"),
            (RuleKey.DressConstraintClausesLayout, "auto"),
            (RuleKey.DressMemberAccessChainsLayout, "auto"),
            (RuleKey.DressBinaryExpressionsLayout, "auto"),
            (RuleKey.DressConditionalExpressionsLayout, "auto"),
            (RuleKey.DressQueryClausesLayout, "auto"),
            (RuleKey.DressAttributesLayout, "auto"),
            (RuleKey.DressBlankLinesAroundNamespaces, "1"),
            (RuleKey.DressBlankLinesAroundTypes, "1"),
            (RuleKey.DressBlankLinesBetweenMembers, "1"),
            (RuleKey.DressBlankLinesBetweenUsingGroups, "1"),
            (RuleKey.DressBlankLinesBetweenMemberCategories, "1"),
            (RuleKey.DressMaxConsecutiveBlankLines, "1"),
            (RuleKey.DressLineCommentSpacing, "single"),
            (RuleKey.DressBlockCommentSpacing, "single"),
            (RuleKey.DressAttachedCommentPlacement, "auto"),
            (RuleKey.DressXmlCommentPlacement, "attached"),
            (RuleKey.DressXmlElementLayout, "single_line"),
            (RuleKey.DressGlobalUsingOrder, "first"),
            (RuleKey.DressUsingKindOrder, "ordinary,static,alias"),
            (RuleKey.CSharpPreferredModifierOrder, "public,protected,internal,private,file,new,static,abstract,virtual,sealed,override,readonly,unsafe,required,volatile,async"),
            (RuleKey.DressObjectInitializerIndentation, "indented"),
            (RuleKey.DressCollectionInitializerIndentation, "indented"),
            (RuleKey.DressArrayInitializerIndentation, "indented"),
            (RuleKey.DressWithInitializerIndentation, "indented"),
            (RuleKey.DressCollectionExpressionIndentation, "not_indented"),
            (RuleKey.DressMethodBody, "expression"),
            (RuleKey.DressConstructorBody, "expression"),
            (RuleKey.DressOperatorBody, "expression"),
            (RuleKey.DressPropertyBody, "expression"),
            (RuleKey.DressIndexerBody, "expression"),
            (RuleKey.DressAccessorBody, "expression"),
            (RuleKey.DressLambdaBody, "expression"),
            (RuleKey.DressNamespaceStyle, "file_scoped"),
            (RuleKey.DressConditionalBraces, "balanced")
        ];

    static readonly Dictionary<RuleKey, string> DefaultsByKey = Defaults.ToDictionary(item => item.Key, item => item.Default);

    internal static bool IsValid(RuleKey key, string value)
    {
        value = NormalizeValue(key, value);
        if (value.Equals("unset", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!DefaultsByKey.TryGetValue(key, out var sample))
            return true;
        var name = key.ToName();
        if (name == "csharp_new_line_before_open_brace")
            return value.Equals("all", StringComparison.OrdinalIgnoreCase) || value.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                IsSubset(value, "accessors anonymous_methods anonymous_types control_blocks events indexers lambdas local_functions methods object_collection_array_initializers properties types");
        if (name == "csharp_space_between_parentheses")
            return value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                IsSubset(value, "control_flow_statements expressions type_casts");
        if (BooleanValues.Contains(sample))
            return BooleanValues.Contains(value);
        if (name == "indent_size" && value.Equals("tab", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name == "max_line_length" && value.Equals("off", StringComparison.OrdinalIgnoreCase))
            return true;
        if (int.TryParse(sample, out _))
        {
            var permitsZero = name.StartsWith("dress_blank_lines_", StringComparison.Ordinal) ||
                name == "dress_max_consecutive_blank_lines";
            return int.TryParse(value, out var number) && (permitsZero ? number >= 0 : number > 0);
        }

        if (name == "dress_using_kind_order")
            return IsPermutation(value, ["ordinary", "static", "alias"]);
        if (name == "csharp_preferred_modifier_order")
            return IsPermutation(value, sample.Split(','));
        var allowed = name switch
            {
                "charset" => "latin1 utf-8 utf-8-bom utf-16be utf-16le",
                "end_of_line" => "cr lf crlf",
                "indent_style" => "space tab",
                "csharp_new_line_before_open_brace" => "all none accessors anonymous_methods anonymous_types control_blocks events indexers lambdas local_functions methods object_collection_array_initializers properties types",
                "csharp_indent_labels" => "flush_left no_change one_less_than_current",
                "csharp_space_around_binary_operators" => "before_and_after ignore none",
                "csharp_space_between_parentheses" => "control_flow_statements expressions type_casts false",
                var n when n.EndsWith("_layout", StringComparison.Ordinal) && n != "dress_xml_element_layout" => "always_single auto always_multi",
                var n when n.EndsWith("_indentation", StringComparison.Ordinal) => "indented not_indented",
                "dress_xml_element_layout" => "single_line multi_line",
                "dress_line_comment_spacing" or "dress_block_comment_spacing" => "none single",
                "dress_attached_comment_placement" => "same_line own_line auto",
                "dress_xml_comment_placement" => "attached separated",
                "dress_namespace_style" => "file_scoped block_scoped",
                "dress_conditional_braces" => "compact always balanced",
                "dress_global_using_order" => "first last mixed",
                var n when n.EndsWith("_body", StringComparison.Ordinal) => "block expression",
                _ => sample
            };
        return allowed.Split(' ').Contains(value, StringComparer.OrdinalIgnoreCase);
    }

    internal static string Normalize(RuleKey key, string value) => NormalizeValue(key, value).ToLowerInvariant();

    static string NormalizeValue(RuleKey key, string value)
    {
        value = value.Trim();
        var name = key.ToName();
        return name.StartsWith("csharp_", StringComparison.Ordinal) || name.StartsWith("dotnet_", StringComparison.Ordinal)
            ? WithoutSeverity(value)
            : value;
    }

    static string WithoutSeverity(string value)
    {
        var colon = value.IndexOf(':');
        return colon < 0 ? value : value[..colon].TrimEnd();
    }

    static bool IsPermutation(string value, IReadOnlyCollection<string> allowed)
    {
        var values = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return values.Length == allowed.Count
            && values.Distinct(StringComparer.OrdinalIgnoreCase).Count() == allowed.Count
            && values.All(item => allowed.Contains(item, StringComparer.OrdinalIgnoreCase));
    }

    static bool IsSubset(string value, string allowed)
    {
        var values = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var allowedValues = allowed.Split(' ');
        return values.Length > 0 && values.Distinct(StringComparer.OrdinalIgnoreCase).Count() == values.Length
            && values.All(item => allowedValues.Contains(item, StringComparer.OrdinalIgnoreCase));
    }
}