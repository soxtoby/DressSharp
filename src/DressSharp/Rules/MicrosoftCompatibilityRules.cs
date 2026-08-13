using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

sealed class MicrosoftSpacingRule(string key, int order) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new(
        key,
        key switch
            {
                "csharp_space_around_binary_operators" => ["before_and_after", "ignore", "none"],
                "csharp_space_between_parentheses" => ParenthesisValues(),
                _ => ["true", "false"]
            },
        "token spacing",
        RuleSafetyClass.Layout,
        "Only same-line whitespace changes",
        order);

    public SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context)
    {
        if (preference == "ignore")
            return root;

        if (key == "csharp_space_between_empty_square_brackets")
            return root.ReplaceNodes(
                root.DescendantNodes()
                    .OfType<ArrayRankSpecifierSyntax>()
                    .Where(x => x.Sizes.Count == 1
                        && x.Sizes[0].IsKind(SyntaxKind.OmittedArraySizeExpression)
                        && SyntaxRuleSafety.CanRewrite(x, context)),
                (node, rewritten) =>
                    {
                        var omitted = rewritten.Sizes[0].WithoutTrivia();
                        var closeTrivia = preference == "true" ? SyntaxFactory.TriviaList(SyntaxFactory.Space) : default;
                        var close = rewritten.CloseBracketToken.WithLeadingTrivia(closeTrivia);
                        return rewritten
                            .WithOpenBracketToken(rewritten.OpenBracketToken
                                .WithTrailingTrivia(default(SyntaxTriviaList)))
                            .WithSizes(SyntaxFactory.SeparatedList<ExpressionSyntax>([omitted]))
                            .WithCloseBracketToken(close);
                    });
        var tokens = root.DescendantTokens().ToArray();
        var replacements = new Dictionary<SyntaxToken, SyntaxToken>();
        for (var i = 0; i + 1 < tokens.Length; i++)
        {
            var left = tokens[i];
            var right = tokens[i + 1];
            var desired = DesiredSpace(left, right, preference);
            if (desired is null || context.IsUnsafe(left) || context.IsUnsafe(right) || HasBoundary(left.LeadingTrivia, left.TrailingTrivia) || HasBoundary(right.LeadingTrivia, right.TrailingTrivia))
                continue;
            var spacingToken = IsBinaryOperator(left) ? left : IsBinaryOperator(right) ? right : default;
            if (spacingToken != default && spacingToken.Parent!.DescendantTrivia(descendIntoTrivia: true).Any(t => t.IsDirective || t.IsComment()))
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

    bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string value)
    {
        var enabled = value == "true";
        return key switch
            {
                "csharp_space_after_cast" when left.IsKind(SyntaxKind.CloseParenToken) && left.Parent is CastExpressionSyntax => enabled,
                "csharp_space_after_keywords_in_control_flow_statements" when IsControlKeyword(left) && right.IsKind(SyntaxKind.OpenParenToken) => enabled,
                "csharp_space_between_parentheses" => ParenthesisSpace(left, right, value),
                "csharp_space_before_colon_in_inheritance_clause" when right.IsKind(SyntaxKind.ColonToken) && right.Parent is BaseListSyntax => enabled,
                "csharp_space_after_colon_in_inheritance_clause" when left.IsKind(SyntaxKind.ColonToken) && left.Parent is BaseListSyntax => enabled,
                "csharp_space_around_binary_operators" when IsBinaryOperator(left) || IsBinaryOperator(right) => value == "before_and_after",
                "csharp_space_between_method_declaration_parameter_list_parentheses" when IsNonEmptyDeclarationParenPair(left, right) => enabled,
                "csharp_space_between_method_declaration_empty_parameter_list_parentheses" when IsEmptyDeclarationParenPair(left, right) => enabled,
                "csharp_space_between_method_declaration_name_and_open_parenthesis" when right.IsKind(SyntaxKind.OpenParenToken) && right.Parent is ParameterListSyntax => enabled,
                "csharp_space_between_method_call_parameter_list_parentheses" when IsNonEmptyCallParenPair(left, right) => enabled,
                "csharp_space_between_method_call_empty_parameter_list_parentheses" when IsEmptyCallParenPair(left, right) => enabled,
                "csharp_space_between_method_call_name_and_opening_parenthesis" when right.IsKind(SyntaxKind.OpenParenToken) && right.Parent?.Parent is InvocationExpressionSyntax => enabled,
                "csharp_space_after_comma" when left.IsKind(SyntaxKind.CommaToken) => enabled,
                "csharp_space_before_comma" when right.IsKind(SyntaxKind.CommaToken) => enabled,
                "csharp_space_after_dot" when left.IsKind(SyntaxKind.DotToken) => enabled,
                "csharp_space_before_dot" when right.IsKind(SyntaxKind.DotToken) => enabled,
                "csharp_space_after_semicolon_in_for_statement" when left.IsKind(SyntaxKind.SemicolonToken) && left.Parent is ForStatementSyntax => enabled,
                "csharp_space_before_semicolon_in_for_statement" when right.IsKind(SyntaxKind.SemicolonToken) && right.Parent is ForStatementSyntax => enabled,
                "csharp_space_around_declaration_statements" when (left.IsKind(SyntaxKind.EqualsToken) || right.IsKind(SyntaxKind.EqualsToken)) && (left.Parent is EqualsValueClauseSyntax || right.Parent is EqualsValueClauseSyntax) => enabled,
                "csharp_space_before_open_square_brackets" when right.IsKind(SyntaxKind.OpenBracketToken) && right.Parent is BracketedArgumentListSyntax or BracketedParameterListSyntax or ArrayRankSpecifierSyntax => enabled,
                "csharp_space_between_empty_square_brackets" when IsEmptyBracketEdge(right) => enabled,
                "csharp_space_between_square_brackets" when IsNonEmptyBracketEdge(left, right) => enabled,
                _ => null
            };
    }

    static bool? ParenthesisSpace(SyntaxToken left, SyntaxToken right, string value)
    {
        var parent = left.IsKind(SyntaxKind.OpenParenToken) ? left.Parent : right.IsKind(SyntaxKind.CloseParenToken) ? right.Parent : null;
        if (parent is null)
            return null;
        var category = parent is CastExpressionSyntax ? "type_casts" : parent is IfStatementSyntax or WhileStatementSyntax or ForStatementSyntax or ForEachStatementSyntax or SwitchStatementSyntax or LockStatementSyntax or UsingStatementSyntax or CatchDeclarationSyntax ? "control_flow_statements" : "expressions";
        return value.Split(',', StringSplitOptions.TrimEntries).Contains(category);
    }

    static bool IsControlKeyword(SyntaxToken token) => token.IsKind(SyntaxKind.IfKeyword) || token.IsKind(SyntaxKind.ForKeyword) || token.IsKind(SyntaxKind.ForEachKeyword) || token.IsKind(SyntaxKind.WhileKeyword) || token.IsKind(SyntaxKind.SwitchKeyword) || token.IsKind(SyntaxKind.LockKeyword) || token.IsKind(SyntaxKind.UsingKeyword) || token.IsKind(SyntaxKind.CatchKeyword);

    static System.Collections.Immutable.ImmutableArray<string> ParenthesisValues()
    {
        var categories = new[] { "control_flow_statements", "expressions", "type_casts" };
        return ["false", .. Enumerable.Range(1, categories.Length).SelectMany(length => categories.Permute().Where(permutation => permutation.Count() == categories.Length).Select(permutation => string.Join(',', permutation.Take(length)))).Distinct()];
    }

    static bool IsBinaryOperator(SyntaxToken token) => token.Parent switch
        {
            BinaryExpressionSyntax binary => token == binary.OperatorToken,
            AssignmentExpressionSyntax assignment => token == assignment.OperatorToken,
            _ => false
        };

    static bool IsNonEmptyDeclarationParenPair(SyntaxToken l, SyntaxToken r) => Parent<ParameterListSyntax>(l, r) is { Parameters.Count: > 0 };
    static bool IsEmptyDeclarationParenPair(SyntaxToken l, SyntaxToken r) => l.IsKind(SyntaxKind.OpenParenToken) && r.IsKind(SyntaxKind.CloseParenToken) && l.Parent is ParameterListSyntax list && list.Parameters.Count == 0;
    static bool IsNonEmptyCallParenPair(SyntaxToken l, SyntaxToken r) => Parent<ArgumentListSyntax>(l, r) is { Arguments.Count: > 0, Parent: InvocationExpressionSyntax };
    static bool IsEmptyCallParenPair(SyntaxToken l, SyntaxToken r) => l.IsKind(SyntaxKind.OpenParenToken) && r.IsKind(SyntaxKind.CloseParenToken) && l.Parent is ArgumentListSyntax { Arguments.Count: 0, Parent: InvocationExpressionSyntax };
    static T? Parent<T>(SyntaxToken l, SyntaxToken r) where T : SyntaxNode => l.IsKind(SyntaxKind.OpenParenToken) ? l.Parent as T : r.IsKind(SyntaxKind.CloseParenToken) ? r.Parent as T : null;

    static bool IsEmptyBracketEdge(SyntaxToken right) =>
        right.IsKind(SyntaxKind.CloseBracketToken)
        && right.Parent is ArrayRankSpecifierSyntax { Sizes.Count: 1 } rank
        && rank.Sizes[0].IsKind(SyntaxKind.OmittedArraySizeExpression);

    static bool IsNonEmptyBracketEdge(SyntaxToken left, SyntaxToken right) =>
        (left.IsKind(SyntaxKind.OpenBracketToken)
            && left.Parent is BracketedArgumentListSyntax or ArrayRankSpecifierSyntax
            || right.IsKind(SyntaxKind.CloseBracketToken)
            && right.Parent is BracketedArgumentListSyntax or ArrayRankSpecifierSyntax)
        && !(left.Parent?.FirstAncestorOrSelf<ArrayRankSpecifierSyntax>() is { Sizes.Count: 1 } rank
            && rank.Sizes[0].IsKind(SyntaxKind.OmittedArraySizeExpression))
        && !(right.Parent?.FirstAncestorOrSelf<ArrayRankSpecifierSyntax>() is { Sizes.Count: 1 } rightRank
            && rightRank.Sizes[0].IsKind(SyntaxKind.OmittedArraySizeExpression));

    static bool HasBoundary(SyntaxTriviaList left, SyntaxTriviaList right) =>
        left.Concat(right)
            .Any(t => t.IsDirective
                || t.IsComment()
                || t.IsKind(SyntaxKind.EndOfLineTrivia)
                || t.IsKind(SyntaxKind.DisabledTextTrivia));

    static SyntaxTriviaList WithoutWhitespace(SyntaxTriviaList trivia) =>
        SyntaxFactory.TriviaList(trivia.Where(t => !t.IsKind(SyntaxKind.WhitespaceTrivia)));

    static SyntaxTriviaList WithSpace(SyntaxTriviaList trivia, bool space) =>
        space
            ? trivia.Insert(0, SyntaxFactory.Space)
            : trivia;
}

sealed class SingleLinePreservationRule(string key, int order) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new(key, ["true", "false"], "existing single-line constructs", RuleSafetyClass.Layout, "False expands safe single-line occurrences", order);

    public SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context)
    {
        if (preference == "true")
            return root;

        if (key.EndsWith("blocks", StringComparison.Ordinal))
            return root.ReplaceNodes(
                root.DescendantNodes()
                    .OfType<BlockSyntax>()
                    .Where(x => IsSingleLine(x) && SyntaxRuleSafety.CanRewrite(x, context)),
                (node, rewritten) => rewritten
                    .WithOpenBraceToken(rewritten.OpenBraceToken.WithTrailingTrivia(SyntaxFactory.EndOfLine(context.LineEnding)))
                    .WithCloseBraceToken(rewritten.CloseBraceToken.WithLeadingTrivia(SyntaxFactory.EndOfLine(context.LineEnding))));

        return root.ReplaceNodes(
            root.DescendantNodes()
                .OfType<BlockSyntax>()
                .Where(x => IsSingleLine(x) && SyntaxRuleSafety.CanRewrite(x, context)),
            (node, rewritten) => rewritten.WithStatements(SyntaxFactory.List(rewritten.Statements.Select((statement, index) =>
                {
                    if (index > 0)
                        statement = statement.WithLeadingTrivia(SyntaxFactory.TriviaList(statement.GetLeadingTrivia().Where(t => !t.IsKind(SyntaxKind.WhitespaceTrivia))));
                    return index < rewritten.Statements.Count - 1
                        ? statement.WithTrailingTrivia(SyntaxFactory.EndOfLine(context.LineEnding))
                        : statement;
                }))));
    }

    static bool IsSingleLine(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line == node.GetLocation().GetLineSpan().EndLinePosition.Line;
}

sealed class MicrosoftUsingRule(string key, int order) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new(key, ["true", "false"], "using directive groups", RuleSafetyClass.Layout, "Using directives remain stable within sort groups", order);

    public SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context)
    {
        var unit = (CompilationUnitSyntax)root;
        unit = unit.WithUsings(Rewrite(unit.Usings, preference == "true", context));
        return unit.ReplaceNodes(
            unit.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>(),
            (node, rewritten) => rewritten.WithUsings(Rewrite(rewritten.Usings, preference == "true", context)));
    }

    SyntaxList<UsingDirectiveSyntax> Rewrite(SyntaxList<UsingDirectiveSyntax> source, bool enabled, RuleContext context)
    {
        if (source.Count < 2 || source.Any(x => !SyntaxRuleSafety.CanRewrite(x, context)))
            return source;

        if (key == "dotnet_sort_system_directives_first")
            return enabled ? SyntaxFactory.List(source.OrderBy(x => IsSystem(x) ? 0 : 1)) : source;

        var result = source.ToArray();
        for (var i = 1; i < result.Length; i++)
        {
            if (IsSystem(result[i - 1]) == IsSystem(result[i]))
                continue;
            var leading = result[i].GetLeadingTrivia();
            var eols = leading.Count(t => t.IsKind(SyntaxKind.EndOfLineTrivia));
            if (enabled && eols < 2)
            {
                leading = leading.Insert(0, SyntaxFactory.EndOfLine(context.LineEnding));
            }
            else if (!enabled && eols > 1)
            {
                var removed = false;
                leading = SyntaxFactory.TriviaList(leading.Where(t => !t.IsKind(SyntaxKind.EndOfLineTrivia) || removed || (removed = true)));
            }

            result[i] = result[i].WithLeadingTrivia(leading);
        }

        return SyntaxFactory.List(result);
    }

    static bool IsSystem(UsingDirectiveSyntax directive) =>
        directive.Name?.ToString() is { } name
        && (name == "System" || name.StartsWith("System.", StringComparison.Ordinal));
}