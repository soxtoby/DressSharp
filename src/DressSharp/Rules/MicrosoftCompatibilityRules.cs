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

    public virtual SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context)
    {
        var tokens = root.DescendantTokens().ToArray();
        var replacements = new Dictionary<SyntaxToken, SyntaxToken>();
        for (var index = 0; index + 1 < tokens.Length; index++)
        {
            var left = tokens[index];
            var right = tokens[index + 1];
            var desired = DesiredSpace(left, right, preference);
            if (desired is null || IsUnsafe(left, right, context))
                continue;

            var rewrittenLeft = replacements.GetValueOrDefault(left, left);
            var rewrittenRight = replacements.GetValueOrDefault(right, right);
            if (rewrittenLeft.Span.IsEmpty)
                rewrittenLeft = rewrittenLeft.WithLeadingTrivia(WithoutWhitespace(rewrittenLeft.LeadingTrivia));
            rewrittenLeft = rewrittenLeft.WithTrailingTrivia(WithoutWhitespace(rewrittenLeft.TrailingTrivia));
            rewrittenRight = rewrittenRight.WithLeadingTrivia(WithSpace(WithoutWhitespace(rewrittenRight.LeadingTrivia), desired.Value));
            replacements[left] = rewrittenLeft;
            replacements[right] = rewrittenRight;
        }

        return root.ReplaceTokens(replacements.Keys, (token, _) => replacements[token]);
    }

    protected abstract bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string preference);

    protected virtual bool IsUnsafe(SyntaxToken left, SyntaxToken right, RuleContext context) =>
        context.IsUnsafe(left)
        || context.IsUnsafe(right)
        || HasBoundary(left.LeadingTrivia, left.TrailingTrivia)
        || HasBoundary(right.LeadingTrivia, right.TrailingTrivia);

    static bool HasBoundary(SyntaxTriviaList left, SyntaxTriviaList right) =>
        left.Concat(right)
            .Any(trivia =>
                trivia.IsDirective
                || trivia.IsComment()
                || trivia.IsKind(SyntaxKind.EndOfLineTrivia)
                || trivia.IsKind(SyntaxKind.DisabledTextTrivia));

    static SyntaxTriviaList WithoutWhitespace(SyntaxTriviaList trivia) =>
        SyntaxFactory.TriviaList(trivia.Where(item => !item.IsKind(SyntaxKind.WhitespaceTrivia)));

    static SyntaxTriviaList WithSpace(SyntaxTriviaList trivia, bool space) =>
        space ? trivia.Insert(0, SyntaxFactory.Space) : trivia;
}

sealed class CastSpacingRule(int order) : TokenSpacingRule(
    "csharp_space_after_cast",
        ["true", "false"],
    "cast expressions",
    order)
{
    protected override bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string preference) =>
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
    protected override bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string preference) =>
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
    protected override bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string preference)
    {
        var parent = left.IsKind(SyntaxKind.OpenParenToken)
            ? left.Parent
            : right.IsKind(SyntaxKind.CloseParenToken)
                ? right.Parent
                : null;

        if (parent is null)
            return null;

        var category = parent switch
            {
                CastExpressionSyntax => "type_casts",
                IfStatementSyntax or WhileStatementSyntax or ForStatementSyntax or ForEachStatementSyntax
                    or SwitchStatementSyntax or LockStatementSyntax or UsingStatementSyntax or CatchDeclarationSyntax
                    => "control_flow_statements",
                _ => "expressions"
            };
        return preference.Split(',', StringSplitOptions.TrimEntries).Contains(category);
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
    protected override bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string preference)
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
    protected override bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string preference)
    {
        return preference == "ignore" ? null
            : IsOperator(left) || IsOperator(right) ? preference == "before_and_after"
            : null;
    }

    protected override bool IsUnsafe(SyntaxToken left, SyntaxToken right, RuleContext context)
    {
        var token = IsOperator(left) ? left
            : IsOperator(right) ? right
            : default;
        return base.IsUnsafe(left, right, context)
            || token != default && token.Parent!.DescendantTrivia(descendIntoTrivia: true).Any(trivia => trivia.IsDirective || trivia.IsComment());
    }

    static bool IsOperator(SyntaxToken token) => token.Parent switch
        {
            BinaryExpressionSyntax binary => token == binary.OperatorToken,
            AssignmentExpressionSyntax assignment => token == assignment.OperatorToken,
            _ => false
        };
}

enum ParenthesisSpacingKind
{
    Contents,
    EmptyContents,
    BeforeOpening
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
    protected override bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string preference) =>
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
    protected override bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string preference) =>
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

abstract class PunctuationSpacingRule(string key, SyntaxKind tokenKind, SpacingSide side, string ownedSyntax, int order)
    : TokenSpacingRule(key, ["true", "false"], ownedSyntax, order)
{
    protected override bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string preference)
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
    protected override bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string preference) =>
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

    protected override bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string preference) => kind switch
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
        return SyntaxFactory.TriviaList(trivia.Where(item =>
            !item.IsKind(SyntaxKind.EndOfLineTrivia) || removed || (removed = true)));
    }
}
