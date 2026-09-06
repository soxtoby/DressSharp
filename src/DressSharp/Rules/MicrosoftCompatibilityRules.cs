using System.Collections.Immutable;
using DressSharp.Architecture;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

abstract class TokenSpacingRule(
    RuleKey ruleKey,
    string caption,
    string? subgroupName,
    RuleValueDefinition values,
    string defaultValue,
    string ownedSyntax
) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        RuleKey = ruleKey,
        Caption = caption,
        ExpandedCaption = RuleMetadata.Humanize(ruleKey.ToName()),
        GroupName = "Spacing",
        SubgroupName = subgroupName,
        Description = $"Controls {ownedSyntax}. Only same-line whitespace changes.",
        Values = values,
        DefaultValue = defaultValue,
        Example = """
            class Example : Base
            {
                int Add(int left,int right) => left+right;
                void Run() { if(true) Add(1,2); }
            }
            """,
        OwnedSyntax = ownedSyntax,
        Invariant = "Only same-line whitespace changes"
    };

    /// <summary>
    /// The token kinds that can make <see cref="DesiredSpace"/> claim a pair, on either side. A pair
    /// where neither token has one of these kinds never reaches the rule.
    /// </summary>
    internal abstract ImmutableArray<SyntaxKind> TriggerKinds { get; }

    internal abstract bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string preference);

    protected TokenSpacingRule(
        RuleKey ruleKey,
        string caption,
        string? subgroupName,
        ImmutableArray<string> acceptedValues,
        string defaultValue,
        string ownedSyntax)
        : this(ruleKey, caption, subgroupName, RuleValues.From(acceptedValues), defaultValue, ownedSyntax)
    {
    }
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

sealed class CastSpacingRule() : TokenSpacingRule(
    RuleKey.CSharpSpaceAfterCast, "After cast", null,
    ["true", "false"],
    "false",
    "cast expressions")
{
    internal override ImmutableArray<SyntaxKind> TriggerKinds { get; } = [SyntaxKind.CloseParenToken];

    internal override bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string preference) =>
        left.IsKind(SyntaxKind.CloseParenToken) && left.Parent is CastExpressionSyntax
            ? preference == "true"
            : null;
}

sealed class ControlFlowKeywordSpacingRule() : TokenSpacingRule(
    RuleKey.CSharpSpaceAfterKeywordsInControlFlowStatements, "After keywords in control flow statements", null,
    ["true", "false"],
    "true",
    "control-flow keywords")
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

sealed class ParenthesisSpacingRule() : TokenSpacingRule(
    RuleKey.CSharpSpaceBetweenParentheses, "Between parentheses", null,
    RuleValues.MultipleChoice(["control_flow_statements", "expressions", "type_casts"], "false"),
    "false",
    "parenthesized syntax")
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
            ParenthesizedExpressionSyntax => Category.Expressions,
            _ => default
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

sealed class BaseListColonSpacingRule(RuleKey ruleKey, string caption, string? subgroupName, SpacingSide side) : TokenSpacingRule(
    ruleKey, caption, subgroupName,
    ["true", "false"],
    "true",
    "base-list colons")
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

sealed class BinaryOperatorSpacingRule() : TokenSpacingRule(
    RuleKey.CSharpSpaceAroundBinaryOperators, "Around binary operators", null,
    ["before_and_after", "ignore", "none"],
    "before_and_after",
    "binary and assignment operators")
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

sealed class MethodDeclarationSpacingRule(RuleKey ruleKey, string caption, string? subgroupName, ParenthesisSpacingKind kind) : TokenSpacingRule(
    ruleKey, caption, subgroupName,
    ["true", "false"],
    "false",
    "method declaration parameter lists")
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

sealed class MethodCallSpacingRule(RuleKey ruleKey, string caption, string? subgroupName, ParenthesisSpacingKind kind) : TokenSpacingRule(
    ruleKey, caption, subgroupName,
    ["true", "false"],
    "false",
    "invocation argument lists")
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

abstract class PunctuationSpacingRule(RuleKey ruleKey, string caption, string? subgroupName, SyntaxKind tokenKind, SpacingSide side, string defaultValue, string ownedSyntax)
    : TokenSpacingRule(ruleKey, caption, subgroupName, ["true", "false"], defaultValue, ownedSyntax)
{
    internal override ImmutableArray<SyntaxKind> TriggerKinds { get; } = [tokenKind];

    internal override bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string preference)
    {
        var token = side == SpacingSide.Before ? right : left;
        return token.IsKind(tokenKind) && IsOwned(token) ? preference == "true" : null;
    }

    protected virtual bool IsOwned(SyntaxToken token) => true;
}

sealed class CommaSpacingRule(RuleKey ruleKey, string caption, string? subgroupName, SpacingSide side) : PunctuationSpacingRule(
    ruleKey, caption, subgroupName,
    SyntaxKind.CommaToken,
    side,
    side == SpacingSide.After ? "true" : "false",
    "comma separators");

sealed class DotSpacingRule(RuleKey ruleKey, string caption, string? subgroupName, SpacingSide side) : PunctuationSpacingRule(
    ruleKey, caption, subgroupName,
    SyntaxKind.DotToken,
    side,
    "false",
    "member-access dots");

sealed class ForSemicolonSpacingRule(RuleKey ruleKey, string caption, string? subgroupName, SpacingSide side) : PunctuationSpacingRule(
    ruleKey, caption, subgroupName,
    SyntaxKind.SemicolonToken,
    side,
    side == SpacingSide.After ? "true" : "false",
    "for-statement semicolons")
{
    protected override bool IsOwned(SyntaxToken token) => token.Parent is ForStatementSyntax;
}

sealed class DeclarationSpacingRule() : TokenSpacingRule(
    RuleKey.CSharpSpaceAroundDeclarationStatements, "Around declaration statements", null,
    ["false", "ignore"],
    "false",
    "declaration equals tokens")
{
    internal override ImmutableArray<SyntaxKind> TriggerKinds { get; } = [SyntaxKind.EqualsToken];

    internal override bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string preference)
    {
        if (preference == "ignore")
            return null;

        return (left.IsKind(SyntaxKind.EqualsToken)
            || right.IsKind(SyntaxKind.EqualsToken))
            && (left.Parent is EqualsValueClauseSyntax
                || right.Parent is EqualsValueClauseSyntax)
            ? true
            : null;
    }
}

enum BracketSpacingKind
{
    BeforeOpening,
    EmptyContents,
    Contents
}

sealed class BracketSpacingRule(RuleKey ruleKey, string caption, string? subgroupName, BracketSpacingKind kind) : TokenSpacingRule(
    ruleKey, caption, subgroupName,
    ["true", "false"],
    "false",
    "array and element-access brackets")
{
    internal override ImmutableArray<SyntaxKind> TriggerKinds { get; } =
            [SyntaxKind.OpenBracketToken, SyntaxKind.CloseBracketToken];

    internal override bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string preference) => kind switch
        {
            BracketSpacingKind.BeforeOpening when right.IsKind(SyntaxKind.OpenBracketToken)
                && right.Parent is BracketedArgumentListSyntax or BracketedParameterListSyntax or ArrayRankSpecifierSyntax
                => preference == "true",
            BracketSpacingKind.EmptyContents => DesiredEmptyRankSpace(left, right, preference),
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

    static bool? DesiredEmptyRankSpace(SyntaxToken left, SyntaxToken right, string preference)
    {
        var leftRank = EmptyRankFor(left);
        var rightRank = EmptyRankFor(right);
        if (leftRank is null || rightRank is null || leftRank.Span != rightRank.Span)
            return null;

        if (left.IsKind(SyntaxKind.OpenBracketToken)
            && right.IsKind(SyntaxKind.OmittedArraySizeExpressionToken))
        {
            return false;
        }

        return left.IsKind(SyntaxKind.OmittedArraySizeExpressionToken)
            && right.IsKind(SyntaxKind.CloseBracketToken)
                ? preference == "true"
                : null;
    }

    static ArrayRankSpecifierSyntax? EmptyRankFor(SyntaxToken token) =>
        token.Parent?.FirstAncestorOrSelf<ArrayRankSpecifierSyntax>() is { Sizes.Count: 1 } rank
        && rank.Sizes[0].IsKind(SyntaxKind.OmittedArraySizeExpression)
            ? rank
            : null;
}

enum SingleLinePreservationKind
{
    Blocks,
    Statements
}

sealed class SingleLinePreservationRule(RuleKey ruleKey, string caption, string? subgroupName, SingleLinePreservationKind kind) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        RuleKey = ruleKey,
        Caption = caption,
        ExpandedCaption = RuleMetadata.Humanize(ruleKey.ToName()),
        GroupName = "Braces and bodies",
        SubgroupName = subgroupName,
        Description = $"Controls {(kind == SingleLinePreservationKind.Blocks ? "existing single-line blocks and accessor lists" : "adjacent statements and member declarations")}. {(kind == SingleLinePreservationKind.Blocks ? "False expands safe single-line blocks and accessor lists" : "False separates safe adjacent statements and members")}.",
        Values = RuleValues.From(["true", "false"]),
        DefaultValue = "true",
        Example = "class Example { void Run() { Work(); } void Work() { } }",
        OwnedSyntax = kind == SingleLinePreservationKind.Blocks
            ? "existing single-line blocks and accessor lists"
            : "adjacent statements and member declarations",
        Invariant = kind == SingleLinePreservationKind.Blocks
            ? "False expands safe single-line blocks and accessor lists"
            : "False separates safe adjacent statements and members"
    };
}

abstract class UsingDirectiveRule(
    RuleKey ruleKey,
    string defaultValue,
    string invariant
) : IUsingFormattingRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        RuleKey = ruleKey,
        Caption = RuleMetadata.Humanize(ruleKey.ToName()),
        GroupName = "Using directives",
        Description = $"Controls using directive groups. {invariant}.",
        Values = RuleValues.Boolean(),
        DefaultValue = defaultValue,
        Example = """
            using Zeta;
            using System;

            class Example { }
            """,
        OwnedSyntax = "using directive groups",
        Invariant = invariant
    };

    public SyntaxList<UsingDirectiveSyntax> Rewrite(
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

sealed class SystemUsingSortRule() : UsingDirectiveRule(
    RuleKey.DotnetSortSystemDirectivesFirst,
    "true",
    "Using directives remain stable within System groups")
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

sealed class ImportGroupSeparationRule() : UsingDirectiveRule(
    RuleKey.DotnetSeparateImportDirectiveGroups,
    "false",
    "Only System and non-System group boundaries change")
{
    protected override SyntaxList<UsingDirectiveSyntax> Rewrite(
        SyntaxList<UsingDirectiveSyntax> source,
        bool enabled,
        RuleContext context)
    {
        UsingDirectiveSyntax[]? result = null;
        for (var index = 1; index < source.Count; index++)
        {
            var previous = result?[index - 1] ?? source[index - 1];
            var current = result?[index] ?? source[index];
            if (IsSystem(previous) == IsSystem(current))
                continue;

            var trailing = previous.GetTrailingTrivia();
            var leading = current.GetLeadingTrivia();
            var trailingEndOfLines = trailing.Count(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));
            var leadingEndOfLines = leading.Count(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));
            var endOfLines = trailingEndOfLines + leadingEndOfLines;
            var desired = enabled ? 2 : 1;
            if (endOfLines == desired)
                continue;

            while (endOfLines < desired)
            {
                leading = leading.Insert(0, SyntaxFactory.EndOfLine(context.LineEnding));
                endOfLines++;
            }

            while (endOfLines > desired)
            {
                if (leadingEndOfLines > 0)
                {
                    leading = RemoveFirstEndOfLine(leading);
                    leadingEndOfLines--;
                }
                else
                {
                    trailing = RemoveFirstEndOfLine(trailing);
                    trailingEndOfLines--;
                }

                endOfLines--;
            }

            result ??= source.ToArray();
            result[index - 1] = result[index - 1].WithTrailingTrivia(trailing);
            result[index] = result[index].WithLeadingTrivia(leading);
        }

        return result is null ? source : SyntaxFactory.List(result);
    }

    static SyntaxTriviaList RemoveFirstEndOfLine(SyntaxTriviaList trivia)
    {
        for (var index = 0; index < trivia.Count; index++)
        {
            if (trivia[index].IsKind(SyntaxKind.EndOfLineTrivia))
                return trivia.RemoveAt(index);
        }

        return trivia;
    }
}
