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
    /// Whether the rule decides spacing purely from an adjacent token pair, so the pipeline can
    /// run it inside a shared single-pass batch instead of giving it its own walk and rewrite.
    /// </summary>
    internal virtual bool ParticipatesInBatch => true;

    /// <summary>
    /// The token kinds that can make <see cref="DesiredSpace"/> claim a pair, on either side. A pair
    /// where neither token has one of these kinds never reaches the rule.
    /// </summary>
    internal abstract ImmutableArray<SyntaxKind> TriggerKinds { get; }

    public virtual SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context) =>
        TokenSpacingBatch.Apply(root, [new(this, preference)], context);

    internal abstract bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string preference);

    internal virtual bool IsUnsafe(TokenPair pair, RuleContext context) =>
        context.IsUnsafe(pair.Left)
        || context.IsUnsafe(pair.Right)
        || pair.HasBoundary;
}

/// <summary>
/// One adjacent token pair under consideration, carrying the boundary test that every spacing rule
/// shares. The instance is reused across pairs so a batch walk allocates nothing per token.
/// </summary>
sealed class TokenPair
{
    bool? _hasBoundary;

    internal SyntaxToken Left { get; private set; }
    internal SyntaxToken Right { get; private set; }

    internal void Reset(SyntaxToken left, SyntaxToken right)
    {
        Left = left;
        Right = right;
        _hasBoundary = null;
    }

    /// <summary>
    /// True when trivia around the pair carries meaning a spacing rule must not disturb. It depends
    /// only on the pair, so it is computed at most once however many rules ask.
    /// </summary>
    internal bool HasBoundary =>
        _hasBoundary ??=
            ContainsBoundary(Left.LeadingTrivia)
            || ContainsBoundary(Left.TrailingTrivia)
            || ContainsBoundary(Right.LeadingTrivia)
            || ContainsBoundary(Right.TrailingTrivia);

    static bool ContainsBoundary(SyntaxTriviaList trivia)
    {
        return trivia.Any(item =>
            item.IsDirective
            || item.IsComment()
            || item.IsKind(SyntaxKind.EndOfLineTrivia)
            || item.IsKind(SyntaxKind.DisabledTextTrivia));
    }
}

/// <summary>
/// Applies a run of token-spacing rules in a single walk. Running them together is equivalent to
/// running them one after another because each rule fully overwrites the whitespace around the pair
/// it owns, so the last rule in catalog order that claims a pair decides it either way.
/// </summary>
static class TokenSpacingBatch
{
    /// <summary>
    /// How many rules one run can hold, bounded by the width of the per-kind trigger mask.
    /// </summary>
    internal const int MaximumRules = 64;

    internal static SyntaxNode Apply(
        SyntaxNode root,
        ReadOnlySpan<(TokenSpacingRule Rule, string Preference)> rules,
        RuleContext context)
    {
        var triggers = Triggers(rules);
        Dictionary<SyntaxToken, SyntaxToken>? replacements = null;
        var pair = new TokenPair();
        var left = default(SyntaxToken);
        var leftTrigger = 0UL;
        var started = false;
        foreach (var right in root.DescendantTokens())
        {
            var rightTrigger = triggers.GetValueOrDefault(right.RawKind);
            if (!started)
            {
                left = right;
                leftTrigger = rightTrigger;
                started = true;
                continue;
            }

            // Nearly every pair is two tokens no spacing rule owns. Skipping those on a kind lookup
            // keeps the walk off the rules entirely instead of asking each one in turn.
            var candidates = leftTrigger | rightTrigger;
            if (candidates != 0)
            {
                pair.Reset(left, right);
                var desired = default(bool?);
                for (var index = 0; index < rules.Length; index++)
                {
                    if ((candidates & (1UL << index)) == 0)
                        continue;
                    var (rule, preference) = rules[index];
                    var candidate = rule.DesiredSpace(left, right, preference);
                    if (candidate is not null && !rule.IsUnsafe(pair, context))
                        desired = candidate;
                }

                if (desired is not null)
                    Record(ref replacements, left, right, desired.Value);
            }

            left = right;
            leftTrigger = rightTrigger;
        }

        return replacements is null ? root : TokenRewriting.ReplaceTokens(root, replacements);
    }

    /// <summary>
    /// Maps a token kind to the bit set of rules in this run that could claim a pair containing it.
    /// </summary>
    static Dictionary<int, ulong> Triggers(ReadOnlySpan<(TokenSpacingRule Rule, string Preference)> rules)
    {
        var triggers = new Dictionary<int, ulong>();

        for (var index = 0; index < rules.Length; index++)
            foreach (var kind in rules[index].Rule.TriggerKinds)
                triggers[(int)kind] = triggers.GetValueOrDefault((int)kind) | (1UL << index);

        return triggers;
    }

    static void Record(ref Dictionary<SyntaxToken, SyntaxToken>? replacements, SyntaxToken left, SyntaxToken right, bool space)
    {
        var rewrittenLeft = replacements is not null && replacements.TryGetValue(left, out var pendingLeft) ? pendingLeft : left;
        var rewrittenRight = replacements is not null && replacements.TryGetValue(right, out var pendingRight) ? pendingRight : right;

        var changedLeft = false;
        if (rewrittenLeft.Span.IsEmpty && HasWhitespace(rewrittenLeft.LeadingTrivia))
        {
            rewrittenLeft = rewrittenLeft.WithLeadingTrivia(WithoutWhitespace(rewrittenLeft.LeadingTrivia));
            changedLeft = true;
        }

        if (HasWhitespace(rewrittenLeft.TrailingTrivia))
        {
            rewrittenLeft = rewrittenLeft.WithTrailingTrivia(WithoutWhitespace(rewrittenLeft.TrailingTrivia));
            changedLeft = true;
        }

        var changedRight = !LeadingAlreadyDesired(rewrittenRight.LeadingTrivia, space);
        if (changedRight)
            rewrittenRight = rewrittenRight.WithLeadingTrivia(WithSpace(WithoutWhitespace(rewrittenRight.LeadingTrivia), space));

        // Rewriting a token to identical text still forces Roslyn to rebuild its whole ancestor
        // spine, so only record tokens whose trivia actually differs from what the rule wants.
        if (!changedLeft && !changedRight)
            return;

        replacements ??= [];
        if (changedLeft)
            replacements[left] = rewrittenLeft;
        if (changedRight)
            replacements[right] = rewrittenRight;
    }

    static bool HasWhitespace(SyntaxTriviaList trivia) => trivia.Any(item => item.IsKind(SyntaxKind.WhitespaceTrivia));

    static bool LeadingAlreadyDesired(SyntaxTriviaList trivia, bool space)
    {
        var index = 0;
        if (space)
        {
            if (trivia.Count == 0 || !IsSingleSpace(trivia[0]))
                return false;
            index = 1;
        }

        for (; index < trivia.Count; index++)
        {
            if (trivia[index].IsKind(SyntaxKind.WhitespaceTrivia))
                return false;
        }

        return true;
    }

    static bool IsSingleSpace(SyntaxTrivia trivia) =>
        trivia.IsKind(SyntaxKind.WhitespaceTrivia)
        && trivia.FullSpan.Length == 1
        && trivia.ToString() == " ";

    static readonly SyntaxTriviaList SingleSpace = SyntaxFactory.TriviaList(SyntaxFactory.Space);

    static SyntaxTriviaList WithoutWhitespace(SyntaxTriviaList trivia)
    {
        var kept = trivia.Count(item => !item.IsKind(SyntaxKind.WhitespaceTrivia));

        // Trivia between two tokens a spacing rule owns is nearly always whitespace and nothing else,
        // so the usual answer is the empty list, which costs no allocation at all.
        if (kept == 0)
            return default;
        if (kept == trivia.Count)
            return trivia;

        var retained = new List<SyntaxTrivia>(kept);
        retained.AddRange(trivia
            .Where(item => !item.IsKind(SyntaxKind.WhitespaceTrivia)));

        return SyntaxFactory.TriviaList(retained);
    }

    static SyntaxTriviaList WithSpace(SyntaxTriviaList trivia, bool space) =>
        !space ? trivia
        : trivia.Count == 0 ? SingleSpace
        : trivia.Insert(0, SyntaxFactory.Space);
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

    internal override bool IsUnsafe(TokenPair pair, RuleContext context)
    {
        var token = IsOperator(pair.Left) ? pair.Left
            : IsOperator(pair.Right) ? pair.Right
            : default;
        return base.IsUnsafe(pair, context)
            || token != default && token.Parent!.DescendantTrivia(descendIntoTrivia: true)
                .Any(trivia => trivia.IsDirective || trivia.IsComment());
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
    EmptyContents,
    Contents
}

sealed class BracketSpacingRule(BracketSpacingKind kind, int order) : TokenSpacingRule(
    kind switch
        {
            BracketSpacingKind.BeforeOpening => "csharp_space_before_open_square_brackets",
            BracketSpacingKind.EmptyContents => "csharp_space_between_empty_square_brackets",
            _ => "csharp_space_between_square_brackets"
        },
        ["true", "false"],
    "array and element-access brackets",
    order)
{
    // The empty-brackets variant rewrites whole rank specifiers rather than deciding an adjacent
    // token pair, so it keeps its own pass instead of joining the shared spacing batch.
    internal override bool ParticipatesInBatch => kind != BracketSpacingKind.EmptyContents;

    public override SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context)
    {
        if (kind != BracketSpacingKind.EmptyContents)
            return base.Transform(root, preference, context);

        return root.ReplaceNodes(
            root.DescendantNodes()
                .OfType<ArrayRankSpecifierSyntax>()
                .Where(rank =>
                    rank.Sizes.Count == 1
                    && rank.Sizes[0].IsKind(SyntaxKind.OmittedArraySizeExpression)
                    && SyntaxRuleSafety.CanRewrite(rank, context)),
            (node, rewritten) => RewriteEmpty(rewritten, preference == "true"));
    }

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

    static ArrayRankSpecifierSyntax RewriteEmpty(ArrayRankSpecifierSyntax rank, bool space)
    {
        var omitted = rank.Sizes[0].WithoutTrivia();
        var closeTrivia = space ? SyntaxFactory.TriviaList(SyntaxFactory.Space) : default;
        return rank
            .WithOpenBracketToken(rank.OpenBracketToken.WithTrailingTrivia(default(SyntaxTriviaList)))
            .WithSizes(SyntaxFactory.SeparatedList<ExpressionSyntax>([omitted]))
            .WithCloseBracketToken(rank.CloseBracketToken.WithLeadingTrivia(closeTrivia));
    }

    static bool IsNonEmptyEdge(SyntaxToken left, SyntaxToken right) =>
        (left.IsKind(SyntaxKind.OpenBracketToken) && left.Parent is BracketedArgumentListSyntax or ArrayRankSpecifierSyntax
            || right.IsKind(SyntaxKind.CloseBracketToken) && right.Parent is BracketedArgumentListSyntax or ArrayRankSpecifierSyntax)
        && !IsEmptyRank(left)
        && !IsEmptyRank(right);

    static bool IsEmptyRank(SyntaxToken token) =>
        token.Parent?.FirstAncestorOrSelf<ArrayRankSpecifierSyntax>() is { Sizes.Count: 1 } rank
        && rank.Sizes[0].IsKind(SyntaxKind.OmittedArraySizeExpression);
}

sealed class SingleLineBlockPreservationRule(int order) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new("csharp_preserve_single_line_blocks", ["true", "false"], "existing single-line blocks", RuleSafetyClass.Layout, "False expands safe single-line blocks", order);

    public SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context) => preference == "true"
        ? root
        : root.ReplaceNodes(
            root.DescendantNodes().OfType<BlockSyntax>().Where(block => IsSingleLine(block) && SyntaxRuleSafety.CanRewrite(block, context)),
            (node, rewritten) => rewritten
                .WithOpenBraceToken(rewritten.OpenBraceToken.WithTrailingTrivia(SyntaxFactory.EndOfLine(context.LineEnding)))
                .WithCloseBraceToken(rewritten.CloseBraceToken.WithLeadingTrivia(SyntaxFactory.EndOfLine(context.LineEnding))));

    static bool IsSingleLine(SyntaxNode node) =>
        node.GetLocation().GetLineSpan().StartLinePosition.Line == node.GetLocation().GetLineSpan().EndLinePosition.Line;
}

sealed class SingleLineStatementPreservationRule(int order) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new("csharp_preserve_single_line_statements", ["true", "false"], "adjacent single-line statements", RuleSafetyClass.Layout, "False separates safe adjacent statements", order);

    public SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context) => preference == "true"
        ? root
        : root.ReplaceNodes(
            root.DescendantNodes().OfType<BlockSyntax>().Where(block => IsSingleLine(block) && SyntaxRuleSafety.CanRewrite(block, context)),
            (node, rewritten) => rewritten.WithStatements(SyntaxFactory.List(rewritten.Statements.Select((statement, index) => Rewrite(statement, index, rewritten.Statements.Count, context.LineEnding)))));

    static StatementSyntax Rewrite(StatementSyntax statement, int index, int count, string lineEnding)
    {
        if (index > 0)
            statement = statement.WithLeadingTrivia(SyntaxFactory.TriviaList(statement.GetLeadingTrivia().Where(trivia => !trivia.IsKind(SyntaxKind.WhitespaceTrivia))));
        return index < count - 1
            ? statement.WithTrailingTrivia(SyntaxFactory.EndOfLine(lineEnding))
            : statement;
    }

    static bool IsSingleLine(SyntaxNode node) =>
        node.GetLocation().GetLineSpan().StartLinePosition.Line == node.GetLocation().GetLineSpan().EndLinePosition.Line;
}

abstract class UsingDirectiveRule(string key, string invariant, int order) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new(key, ["true", "false"], "using directive groups", RuleSafetyClass.Layout, invariant, order);

    public SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context)
    {
        var unit = (CompilationUnitSyntax)root;
        unit = unit.WithUsings(RewriteSafe(unit.Usings, preference == "true", context));
        return unit.ReplaceNodes(
            unit.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>(),
            (node, rewritten) => rewritten.WithUsings(RewriteSafe(rewritten.Usings, preference == "true", context)));
    }

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
        enabled
            ? SyntaxFactory.List(source.OrderBy(item => IsSystem(item) ? 0 : 1))
            : source;
}

sealed class ImportGroupSeparationRule(int order) : UsingDirectiveRule(
    "dotnet_separate_import_directive_groups",
    "Only the System group boundary changes",
    order)
{
    protected override SyntaxList<UsingDirectiveSyntax> Rewrite(SyntaxList<UsingDirectiveSyntax> source, bool enabled, RuleContext context)
    {
        var result = source.ToArray();
        for (var index = 1; index < result.Length; index++)
        {
            if (IsSystem(result[index - 1]) == IsSystem(result[index]))
                continue;

            var leading = result[index].GetLeadingTrivia();
            var endOfLines = leading.Count(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));
            if (enabled && endOfLines < 2)
                leading = leading.Insert(0, SyntaxFactory.EndOfLine(context.LineEnding));
            else if (!enabled && endOfLines > 1)
                leading = RemoveFirstEndOfLine(leading);
            result[index] = result[index].WithLeadingTrivia(leading);
        }

        return SyntaxFactory.List(result);
    }

    static SyntaxTriviaList RemoveFirstEndOfLine(SyntaxTriviaList trivia)
    {
        var removed = false;
        return SyntaxFactory.TriviaList(trivia
            .Where(item => !item.IsKind(SyntaxKind.EndOfLineTrivia) || removed || (removed = true)));
    }
}