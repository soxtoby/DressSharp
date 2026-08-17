using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

abstract class TokenSpacingRule(
    string key,
    ImmutableArray<string> acceptedValues,
    string ownedSyntax,
    int order
) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new(
        key,
        acceptedValues,
        ownedSyntax,
        RuleSafetyClass.Layout,
        "Only same-line whitespace changes",
        order);

    /// <summary>
    /// The token kinds that can make <see cref="DesiredSpace"/> claim a pair, on either side. A pair
    /// where neither token has one of these kinds never reaches the rule.
    /// </summary>
    internal abstract ImmutableArray<SyntaxKind> TriggerKinds { get; }

    internal abstract bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string preference);

}
/// <summary>
/// One adjacent token pair under consideration. The instance is reused across the emitter walk.
/// </summary>
sealed class TokenPair
{
    internal SyntaxToken Left { get; private set; }
    internal SyntaxToken Right { get; private set; }

    internal void Reset(SyntaxToken left, SyntaxToken right)
    {
        Left = left;
        Right = right;
    }
}

sealed class CastSpacingRule(int order) : TokenSpacingRule(
    "csharp_space_after_cast",
        ["true", "false"],
    "cast expressions",
    order)
{
    internal override ImmutableArray<SyntaxKind> TriggerKinds { get; } = [SyntaxKind.CloseParenToken];

    internal override bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string preference) =>
        left.IsKind(SyntaxKind.CloseParenToken) && left.Parent is CastExpressionSyntax
            ? preference == "true"
            : null;
}

sealed class ControlFlowKeywordSpacingRule(int order) : TokenSpacingRule(
    "csharp_space_after_keywords_in_control_flow_statements",
        ["true", "false"],
    "control-flow keywords",
    order)
{
    internal override ImmutableArray<SyntaxKind> TriggerKinds { get; } = [SyntaxKind.OpenParenToken];

    internal override bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string preference) =>
        right.IsKind(SyntaxKind.OpenParenToken) && IsControlKeyword(left)
            ? preference == "true"
            : null;

    static bool IsControlKeyword(SyntaxToken token) =>
        token.IsKind(SyntaxKind.IfKeyword)
        || token.IsKind(SyntaxKind.ForKeyword)
        || token.IsKind(SyntaxKind.ForEachKeyword)
        || token.IsKind(SyntaxKind.WhileKeyword)
        || token.IsKind(SyntaxKind.SwitchKeyword)
        || token.IsKind(SyntaxKind.LockKeyword)
        || token.IsKind(SyntaxKind.UsingKeyword)
        || token.IsKind(SyntaxKind.CatchKeyword);
}

sealed class ParenthesisSpacingRule(int order) : TokenSpacingRule(
    "csharp_space_between_parentheses",
    AcceptedValues(),
    "parenthesized syntax",
    order)
{
    internal override ImmutableArray<SyntaxKind> TriggerKinds { get; } = [SyntaxKind.OpenParenToken, SyntaxKind.CloseParenToken];

    internal override bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string preference)
    {
        var parent = left.IsKind(SyntaxKind.OpenParenToken) ? left.Parent
            : right.IsKind(SyntaxKind.CloseParenToken) ? right.Parent
            : null;

        if (parent is null)
            return null;

        var category = parent switch
            {
                CastExpressionSyntax => Category.TypeCasts,
                IfStatementSyntax
                    or WhileStatementSyntax
                    or ForStatementSyntax
                    or ForEachStatementSyntax
                    or SwitchStatementSyntax
                    or LockStatementSyntax
                    or UsingStatementSyntax
                    or CatchDeclarationSyntax
                    => Category.ControlFlowStatements,
                _ => Category.Expressions
            };
        return (Selected.GetValueOrDefault(preference) & category) != 0;
    }

    static readonly string[] CategoryNames = ["control_flow_statements", "expressions", "type_casts"];

    /// <summary>
    /// Every accepted preference value mapped to the categories it selects. Splitting the preference
    /// string per token pair dominated this rule's cost, and the value set is fixed and tiny.
    /// </summary>
    static readonly Dictionary<string, Category> Selected = AcceptedValues()
        .ToDictionary(value => value, Parse, StringComparer.OrdinalIgnoreCase);

    static Category Parse(string preference)
    {
        return preference
            .Split(',', StringSplitOptions.TrimEntries)
            .Select(part => Array.IndexOf(CategoryNames, part))
            .Where(index => index >= 0)
            .Aggregate(default(Category), (current, index) => current | (Category)(1 << index));
    }

    static ImmutableArray<string> AcceptedValues()
    {
        var categories = new[] { "control_flow_statements", "expressions", "type_casts" };
        var subsets = Enumerable.Range(1, categories.Length)
            .SelectMany(length => categories.Permute()
                .Select(permutation => string.Join(',', permutation.Take(length))))
            .Distinct();
        return ["false", .. subsets];
    }

    [Flags]
    enum Category
    {
        ControlFlowStatements = 1,
        Expressions = 2,
        TypeCasts = 4
    }
}

enum SpacingSide
{
    Before,
    After
}

sealed class BaseListColonSpacingRule(SpacingSide side, int order) : TokenSpacingRule(
    side == SpacingSide.Before ? "csharp_space_before_colon_in_inheritance_clause" : "csharp_space_after_colon_in_inheritance_clause",
        ["true", "false"],
    "base-list colons",
    order)
{
    internal override ImmutableArray<SyntaxKind> TriggerKinds { get; } = [SyntaxKind.ColonToken];

    internal override bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string preference)
    {
        var token = side == SpacingSide.Before ? right : left;
        return token.IsKind(SyntaxKind.ColonToken) && token.Parent is BaseListSyntax
            ? preference == "true"
            : null;
    }
}

sealed class BinaryOperatorSpacingRule(int order) : TokenSpacingRule(
    "csharp_space_around_binary_operators",
        ["before_and_after", "ignore", "none"],
    "binary and assignment operators",
    order)
{
    // Derived from Roslyn rather than hand-listed so the trigger set cannot drift from IsOperator.
    internal override ImmutableArray<SyntaxKind> TriggerKinds { get; } =
        [
            .. Enum.GetValues<SyntaxKind>()
                .Where(kind =>
                    SyntaxFacts.GetBinaryExpression(kind) != SyntaxKind.None
                    || SyntaxFacts.GetAssignmentExpression(kind) != SyntaxKind.None)
        ];

    internal override bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string preference)
    {
        return preference == "ignore" ? null
            : IsOperator(left) || IsOperator(right) ? preference == "before_and_after"
            : null;
    }

    static bool IsOperator(SyntaxToken token) => token.Parent switch
        {
            BinaryExpressionSyntax binary => token == binary.OperatorToken,
            AssignmentExpressionSyntax assignment => token == assignment.OperatorToken,
            _ => false
        };
}

sealed class MethodDeclarationSpacingRule(ParenthesisSpacingKind kind, int order) : TokenSpacingRule(
    kind switch
        {
            ParenthesisSpacingKind.Contents => "csharp_space_between_method_declaration_parameter_list_parentheses",
            ParenthesisSpacingKind.EmptyContents => "csharp_space_between_method_declaration_empty_parameter_list_parentheses",
            _ => "csharp_space_between_method_declaration_name_and_open_parenthesis"
        },
        ["true", "false"],
    "method declaration parameter lists",
    order)
{
    internal override ImmutableArray<SyntaxKind> TriggerKinds { get; } = [SyntaxKind.OpenParenToken, SyntaxKind.CloseParenToken];

    internal override bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string preference) =>
        Matches(left, right) ? preference == "true" : null;

    bool Matches(SyntaxToken left, SyntaxToken right) => kind switch
        {
            ParenthesisSpacingKind.Contents => Parent<ParameterListSyntax>(left, right) is { Parameters.Count: > 0 },
            ParenthesisSpacingKind.EmptyContents => left.IsKind(SyntaxKind.OpenParenToken)
                && right.IsKind(SyntaxKind.CloseParenToken)
                && left.Parent is ParameterListSyntax { Parameters.Count: 0 },
            _ => right.IsKind(SyntaxKind.OpenParenToken)
                && right.Parent is ParameterListSyntax
        };

    static T? Parent<T>(SyntaxToken left, SyntaxToken right) where T : SyntaxNode =>
        left.IsKind(SyntaxKind.OpenParenToken) ? left.Parent as T
        : right.IsKind(SyntaxKind.CloseParenToken) ? right.Parent as T
        : null;
}

sealed class MethodCallSpacingRule(ParenthesisSpacingKind kind, int order) : TokenSpacingRule(
    kind switch
        {
            ParenthesisSpacingKind.Contents => "csharp_space_between_method_call_parameter_list_parentheses",
            ParenthesisSpacingKind.EmptyContents => "csharp_space_between_method_call_empty_parameter_list_parentheses",
            _ => "csharp_space_between_method_call_name_and_opening_parenthesis"
        },
        ["true", "false"],
    "invocation argument lists",
    order)
{
    internal override ImmutableArray<SyntaxKind> TriggerKinds { get; } = [SyntaxKind.OpenParenToken, SyntaxKind.CloseParenToken];

    internal override bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string preference) =>
        Matches(left, right) ? preference == "true" : null;

    bool Matches(SyntaxToken left, SyntaxToken right) => kind switch
        {
            ParenthesisSpacingKind.Contents => Parent(left, right) is { Arguments.Count: > 0, Parent: InvocationExpressionSyntax },
            ParenthesisSpacingKind.EmptyContents => left.IsKind(SyntaxKind.OpenParenToken)
                && right.IsKind(SyntaxKind.CloseParenToken)
                && left.Parent is ArgumentListSyntax { Arguments.Count: 0, Parent: InvocationExpressionSyntax },
            _ => right.IsKind(SyntaxKind.OpenParenToken)
                && right.Parent?.Parent is InvocationExpressionSyntax
        };

    static ArgumentListSyntax? Parent(SyntaxToken left, SyntaxToken right) =>
        left.IsKind(SyntaxKind.OpenParenToken) ? left.Parent as ArgumentListSyntax : right.IsKind(SyntaxKind.CloseParenToken) ? right.Parent as ArgumentListSyntax : null;
}

enum ParenthesisSpacingKind
{
    Contents,
    EmptyContents,
    BeforeOpening
}

abstract class PunctuationSpacingRule(string key, SyntaxKind tokenKind, SpacingSide side, string ownedSyntax, int order)
    : TokenSpacingRule(key, ["true", "false"], ownedSyntax, order)
{
    internal override ImmutableArray<SyntaxKind> TriggerKinds { get; } = [tokenKind];

    internal override bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string preference)
    {
        var token = side == SpacingSide.Before ? right : left;
        return token.IsKind(tokenKind) && IsOwned(token) ? preference == "true" : null;
    }

    protected virtual bool IsOwned(SyntaxToken token) => true;
}

sealed class CommaSpacingRule(SpacingSide side, int order) : PunctuationSpacingRule(
    side == SpacingSide.Before ? "csharp_space_before_comma" : "csharp_space_after_comma",
    SyntaxKind.CommaToken,
    side,
    "comma separators",
    order);

sealed class DotSpacingRule(SpacingSide side, int order) : PunctuationSpacingRule(
    side == SpacingSide.Before ? "csharp_space_before_dot" : "csharp_space_after_dot",
    SyntaxKind.DotToken,
    side,
    "member-access dots",
    order);

sealed class ForSemicolonSpacingRule(SpacingSide side, int order) : PunctuationSpacingRule(
    side == SpacingSide.Before ? "csharp_space_before_semicolon_in_for_statement" : "csharp_space_after_semicolon_in_for_statement",
    SyntaxKind.SemicolonToken,
    side,
    "for-statement semicolons",
    order)
{
    protected override bool IsOwned(SyntaxToken token) => token.Parent is ForStatementSyntax;
}

sealed class DeclarationSpacingRule(int order) : TokenSpacingRule(
    "csharp_space_around_declaration_statements",
        ["true", "false"],
    "declaration equals tokens",
    order)
{
    internal override ImmutableArray<SyntaxKind> TriggerKinds { get; } = [SyntaxKind.EqualsToken];

    internal override bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string preference) =>
        (left.IsKind(SyntaxKind.EqualsToken)
            || right.IsKind(SyntaxKind.EqualsToken))
        && (left.Parent is EqualsValueClauseSyntax
            || right.Parent is EqualsValueClauseSyntax)
            ? preference == "true"
            : null;
}

enum BracketSpacingKind
{
    BeforeOpening,
    Contents
}

sealed class BracketSpacingRule(BracketSpacingKind kind, int order) : TokenSpacingRule(
    kind switch
        {
            BracketSpacingKind.BeforeOpening => "csharp_space_before_open_square_brackets",
            _ => "csharp_space_between_square_brackets"
        },
        ["true", "false"],
    "array and element-access brackets",
    order)
{
    internal override ImmutableArray<SyntaxKind> TriggerKinds { get; } =
            [SyntaxKind.OpenBracketToken, SyntaxKind.CloseBracketToken];

    internal override bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string preference) => kind switch
        {
            BracketSpacingKind.BeforeOpening when right.IsKind(SyntaxKind.OpenBracketToken)
                && right.Parent is BracketedArgumentListSyntax or BracketedParameterListSyntax or ArrayRankSpecifierSyntax
                => preference == "true",
            BracketSpacingKind.Contents when IsNonEmptyEdge(left, right) => preference == "true",
            _ => null
        };

    static bool IsNonEmptyEdge(SyntaxToken left, SyntaxToken right) =>
        (left.IsKind(SyntaxKind.OpenBracketToken) && left.Parent is BracketedArgumentListSyntax or ArrayRankSpecifierSyntax
            || right.IsKind(SyntaxKind.CloseBracketToken) && right.Parent is BracketedArgumentListSyntax or ArrayRankSpecifierSyntax)
        && !IsEmptyRank(left)
        && !IsEmptyRank(right);

    static bool IsEmptyRank(SyntaxToken token) =>
        token.Parent?.FirstAncestorOrSelf<ArrayRankSpecifierSyntax>() is { Sizes.Count: 1 } rank
        && rank.Sizes[0].IsKind(SyntaxKind.OmittedArraySizeExpression);
}

abstract class UsingDirectiveRule(string key, string invariant, int order) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new(key, ["true", "false"], "using directive groups", RuleSafetyClass.Layout, invariant, order);

    internal SyntaxList<UsingDirectiveSyntax> Rewrite(
        SyntaxList<UsingDirectiveSyntax> source,
        string preference,
        RuleContext context) => RewriteSafe(source, preference == "true", context);

    protected abstract SyntaxList<UsingDirectiveSyntax> Rewrite(SyntaxList<UsingDirectiveSyntax> source, bool enabled, RuleContext context);

    protected static bool IsSystem(UsingDirectiveSyntax directive) =>
        directive.Name?.ToString() is { } name
        && (name == "System" || name.StartsWith("System.", StringComparison.Ordinal));

    SyntaxList<UsingDirectiveSyntax> RewriteSafe(SyntaxList<UsingDirectiveSyntax> source, bool enabled, RuleContext context) =>
        source.Count < 2 || source.Any(item => !SyntaxRuleSafety.CanRewrite(item, context))
            ? source
            : Rewrite(source, enabled, context);
}

sealed class SystemUsingSortRule(int order) : UsingDirectiveRule(
    "dotnet_sort_system_directives_first",
    "Using directives remain stable within System groups",
    order)
{
    protected override SyntaxList<UsingDirectiveSyntax> Rewrite(SyntaxList<UsingDirectiveSyntax> source, bool enabled, RuleContext context) =>
        enabled && IsOutOfOrder(source)
            ? SyntaxFactory.List(source.OrderBy(item => IsSystem(item) ? 0 : 1))
            : source;

    static bool IsOutOfOrder(SyntaxList<UsingDirectiveSyntax> source)
    {
        var sawNonSystem = false;
        foreach (var directive in source)
        {
            if (!IsSystem(directive))
                sawNonSystem = true;
            else if (sawNonSystem)
                return true;
        }

        return false;
    }
}
