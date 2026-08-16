using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

sealed class ConstructLayoutRule(string key, ConstructLayoutKind kind, int order) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new(
        key,
            ["always_single", "auto", "always_multi"],
        kind.ToString(),
        RuleSafetyClass.Layout,
        "Only whitespace owned by the construct changes",
        order);

    internal ConstructLayoutKind Kind => kind;

    public SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context) =>
        ConstructLayoutBatch.Apply(root, [new(this, preference)], context);

    /// <summary>
    /// What one construct kind was asked for: the wrapping outcome, and the line length that the
    /// auto outcome measures against.
    /// </summary>
    internal readonly record struct Setting(string Preference, int Maximum)
    {
        internal static Setting For(string preference, RuleContext context) => new(
            preference,
            preference.Equals("auto", StringComparison.OrdinalIgnoreCase) ? context.MaximumLineLength : int.MaxValue);
    }

    internal sealed class Rewriter(Setting?[] byKind, RuleContext context) : CSharpSyntaxRewriter
    {
        Setting? Enabled(ConstructLayoutKind kind) => byKind[(int)kind];

        public override SyntaxNode? VisitArgumentList(ArgumentListSyntax node)
        {
            var visited = (ArgumentListSyntax)base.VisitArgumentList(node)!;
            return Enabled(ConstructLayoutKind.Arguments) is { } setting ? Delimited(visited, setting) : visited;
        }

        public override SyntaxNode? VisitBracketedArgumentList(BracketedArgumentListSyntax node)
        {
            var visited = (BracketedArgumentListSyntax)base.VisitBracketedArgumentList(node)!;
            return Enabled(ConstructLayoutKind.Arguments) is { } setting ? Delimited(visited, setting) : visited;
        }

        public override SyntaxNode? VisitParameterList(ParameterListSyntax node)
        {
            var visited = (ParameterListSyntax)base.VisitParameterList(node)!;
            return Enabled(ConstructLayoutKind.Parameters) is { } setting ? Delimited(visited, setting) : visited;
        }

        public override SyntaxNode? VisitBracketedParameterList(BracketedParameterListSyntax node)
        {
            var visited = (BracketedParameterListSyntax)base.VisitBracketedParameterList(node)!;
            return Enabled(ConstructLayoutKind.Parameters) is { } setting ? Delimited(visited, setting) : visited;
        }

        public override SyntaxNode? VisitInitializerExpression(InitializerExpressionSyntax node)
        {
            var visited = (InitializerExpressionSyntax)base.VisitInitializerExpression(node)!;
            return Enabled(ConstructLayoutKind.Initializers) is { } setting ? Delimited(visited, setting) : visited;
        }

        public override SyntaxNode? VisitCollectionExpression(CollectionExpressionSyntax node)
        {
            var visited = (CollectionExpressionSyntax)base.VisitCollectionExpression(node)!;
            return Enabled(ConstructLayoutKind.CollectionExpressions) is { } setting ? Delimited(visited, setting) : visited;
        }

        public override SyntaxNode? VisitBaseList(BaseListSyntax node)
        {
            var visited = (BaseListSyntax)base.VisitBaseList(node)!;
            if (Enabled(ConstructLayoutKind.BaseTypeLists) is not { } setting || visited.Types.Count == 0 || Unsafe(visited))
                return visited;
            return LayoutSeparated(visited, visited.Types.GetSeparators(), visited.Types.Select(x => x.GetFirstToken()), setting);
        }

        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node) => Constraints((ClassDeclarationSyntax)base.VisitClassDeclaration(node)!);
        public override SyntaxNode VisitStructDeclaration(StructDeclarationSyntax node) => Constraints((StructDeclarationSyntax)base.VisitStructDeclaration(node)!);
        public override SyntaxNode VisitInterfaceDeclaration(InterfaceDeclarationSyntax node) => Constraints((InterfaceDeclarationSyntax)base.VisitInterfaceDeclaration(node)!);
        public override SyntaxNode VisitRecordDeclaration(RecordDeclarationSyntax node) => Constraints((RecordDeclarationSyntax)base.VisitRecordDeclaration(node)!);

        T Constraints<T>(T visited) where T : TypeDeclarationSyntax
        {
            if (Enabled(ConstructLayoutKind.ConstraintClauses) is not { } setting || visited.ConstraintClauses.Count == 0 || Unsafe(visited))
                return visited;
            return LayoutItems(visited, visited.ConstraintClauses.Select(x => x.GetFirstToken()), setting, continuation: true);
        }

        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var visited = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;
            if (Enabled(ConstructLayoutKind.ConstraintClauses) is not { } setting || visited.ConstraintClauses.Count == 0 || Unsafe(visited))
                return visited;
            return LayoutItems(visited, visited.ConstraintClauses.Select(x => x.GetFirstToken()), setting, continuation: true);
        }

        public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            var visited = (LocalFunctionStatementSyntax)base.VisitLocalFunctionStatement(node)!;
            if (Enabled(ConstructLayoutKind.ConstraintClauses) is not { } setting || visited.ConstraintClauses.Count == 0 || Unsafe(visited))
                return visited;
            return LayoutItems(visited, visited.ConstraintClauses.Select(x => x.GetFirstToken()), setting, continuation: true);
        }

        public override SyntaxNode? VisitDelegateDeclaration(DelegateDeclarationSyntax node)
        {
            var visited = (DelegateDeclarationSyntax)base.VisitDelegateDeclaration(node)!;
            if (Enabled(ConstructLayoutKind.ConstraintClauses) is not { } setting || visited.ConstraintClauses.Count == 0 || Unsafe(visited))
                return visited;
            return LayoutItems(visited, visited.ConstraintClauses.Select(x => x.GetFirstToken()), setting, continuation: true);
        }

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var visited = (MemberAccessExpressionSyntax)base.VisitMemberAccessExpression(node)!;
            if (Enabled(ConstructLayoutKind.MemberAccessChains) is not { } setting || node.Parent is MemberAccessExpressionSyntax || Unsafe(visited))
                return visited;
            var operators = visited.DescendantTokens().Where(x => x.IsKind(SyntaxKind.DotToken) || x.IsKind(SyntaxKind.MinusGreaterThanToken));
            return LayoutItems(visited, operators, setting, continuation: true, compactSingle: true);
        }

        public override SyntaxNode? VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            var visited = (BinaryExpressionSyntax)base.VisitBinaryExpression(node)!;
            if (Enabled(ConstructLayoutKind.BinaryExpressions) is not { } setting || node.Parent is BinaryExpressionSyntax || Unsafe(visited))
                return visited;
            var operators = visited.DescendantNodesAndSelf().OfType<BinaryExpressionSyntax>().Select(x => x.OperatorToken);
            return LayoutItems(visited, operators, setting, continuation: true);
        }

        public override SyntaxNode? VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
            var visited = (ConditionalExpressionSyntax)base.VisitConditionalExpression(node)!;
            if (Enabled(ConstructLayoutKind.ConditionalExpressions) is not { } setting || node.Parent is ConditionalExpressionSyntax || Unsafe(visited))
                return visited;
            return LayoutItems(visited, [visited.QuestionToken, visited.ColonToken], setting, continuation: true);
        }

        public override SyntaxNode? VisitQueryExpression(QueryExpressionSyntax node)
        {
            var visited = (QueryExpressionSyntax)base.VisitQueryExpression(node)!;
            if (Enabled(ConstructLayoutKind.QueryClauses) is not { } setting || Unsafe(visited))
                return visited;
            var starts = visited.Body.Clauses.Select(x => x.GetFirstToken()).Append(visited.Body.SelectOrGroup.GetFirstToken());
            return LayoutItems(visited, starts, setting, continuation: true);
        }

        public override SyntaxNode? VisitAttributeList(AttributeListSyntax node)
        {
            var visited = (AttributeListSyntax)base.VisitAttributeList(node)!;
            return Enabled(ConstructLayoutKind.Attributes) is { } setting ? Delimited(visited, setting) : visited;
        }

        T Delimited<T>(T node, Setting setting) where T : SyntaxNode
        {
            if (Unsafe(node))
                return node;
            var open = node.GetFirstToken();
            var close = node.GetLastToken();
            var separators = DirectSeparators(node).ToArray();
            var elements = DirectElements(node).ToArray();
            if (elements.Length == 0)
                return node;

            var multi = IsMulti(node, setting);
            if (!multi && HasLineComment(node))
                return node;
            var indent = Indent(node, multi ? 1 : 0);
            var replacements = new Dictionary<SyntaxToken, SyntaxToken>();
            replacements[open] = open.WithTrailingTrivia(Trailing(open.TrailingTrivia, multi ? Line(indent) : default));
            foreach (var separator in separators)
                replacements[separator] = separator.WithTrailingTrivia(Trailing(separator.TrailingTrivia, multi ? Line(indent) : SyntaxFactory.TriviaList(SyntaxFactory.Space)));
            replacements[close] = close.WithLeadingTrivia(Leading(close.LeadingTrivia, multi ? Line(Indent(node, 0)) : default));
            foreach (var element in elements)
            {
                var first = element.GetFirstToken();
                var replacement = replacements.GetValueOrDefault(first, first);
                replacements[first] = replacement.WithLeadingTrivia(Leading(replacement.LeadingTrivia, default));
            }

            var beforeClose = close.GetPreviousToken();
            replacements[beforeClose] = (replacements.GetValueOrDefault(beforeClose, beforeClose))
                .WithTrailingTrivia(WithoutWhitespace(beforeClose.TrailingTrivia));
            return node.ReplaceTokens(replacements.Keys, (token, _) => replacements[token]);
        }

        static IEnumerable<SyntaxNode> DirectElements(SyntaxNode node) => node switch
            {
                ArgumentListSyntax n => n.Arguments,
                BracketedArgumentListSyntax n => n.Arguments,
                ParameterListSyntax n => n.Parameters,
                BracketedParameterListSyntax n => n.Parameters,
                InitializerExpressionSyntax n => n.Expressions,
                CollectionExpressionSyntax n => n.Elements,
                AttributeListSyntax n => n.Attributes,
                _ => []
            };

        static IEnumerable<SyntaxToken> DirectSeparators(SyntaxNode node) => node switch
            {
                ArgumentListSyntax n => n.Arguments.GetSeparators(),
                BracketedArgumentListSyntax n => n.Arguments.GetSeparators(),
                ParameterListSyntax n => n.Parameters.GetSeparators(),
                BracketedParameterListSyntax n => n.Parameters.GetSeparators(),
                InitializerExpressionSyntax n => n.Expressions.GetSeparators(),
                CollectionExpressionSyntax n => n.Elements.GetSeparators(),
                AttributeListSyntax n => n.Attributes.GetSeparators(),
                _ => []
            };

        T LayoutSeparated<T>(T node, IEnumerable<SyntaxToken> separators, IEnumerable<SyntaxToken> starts, Setting setting) where T : SyntaxNode
        {
            var multi = IsMulti(node, setting);
            if (!multi && HasLineComment(node))
                return node;
            var indent = Indent(node, 1);
            var replacements = new Dictionary<SyntaxToken, SyntaxToken>();
            var separatorArray = separators.ToArray();
            foreach (var separator in separatorArray)
                replacements[separator] = separator.WithTrailingTrivia(Trailing(separator.TrailingTrivia, multi ? default : SyntaxFactory.TriviaList(SyntaxFactory.Space)));
            var first = true;
            
            foreach (var token in starts)
            {
                var leading = multi ? Line(indent) : first ? SyntaxFactory.TriviaList(SyntaxFactory.Space) : default;
                replacements[token] = token.WithLeadingTrivia(Leading(token.LeadingTrivia, leading));
                var previous = token.GetPreviousToken();
                if (!separatorArray.Contains(previous))
                    replacements[previous] = previous.WithTrailingTrivia(WithoutWhitespace(previous.TrailingTrivia));
                first = false;
            }

            return node.ReplaceTokens(replacements.Keys, (token, _) => replacements[token]);
        }

        T LayoutItems<T>(T node, IEnumerable<SyntaxToken> starts, Setting setting, bool continuation, bool compactSingle = false) where T : SyntaxNode
        {
            var tokens = starts.Distinct().ToArray();
            if (tokens.Length == 0)
                return node;
            
            var multi = IsMulti(node, setting);
            if (!multi && HasLineComment(node))
                return node;
            
            var trivia = multi
                ? Line(Indent(node, continuation ? 1 : 0))
                : compactSingle
                    ? default
                    : SyntaxFactory.TriviaList(SyntaxFactory.Space);
            
            var replacements = new Dictionary<SyntaxToken, SyntaxToken>();
            foreach (var token in tokens)
            {
                replacements[token] = token.WithLeadingTrivia(Leading(token.LeadingTrivia, trivia));
                var previous = token.GetPreviousToken();
                replacements[previous] = (replacements.GetValueOrDefault(previous, previous))
                    .WithTrailingTrivia(WithoutWhitespace(previous.TrailingTrivia));
            }

            return node.ReplaceTokens(replacements.Keys, (token, _) => replacements[token]);
        }

        bool IsMulti(SyntaxNode node, Setting setting)
        {
            if (setting.Preference.Equals("always_multi", StringComparison.OrdinalIgnoreCase))
                return true;
            if (setting.Preference.Equals("always_single", StringComparison.OrdinalIgnoreCase) || setting.Maximum == int.MaxValue)
                return false;
            return VisualStartColumn(node) + SingleLineWidth(node) > setting.Maximum;
        }

        // ContainsDirectives is a flag Roslyn already carries on every node, so it answers what a
        // full descendant-trivia scan of the subtree would, without the scan. The scan ran once per
        // construct, which on nested constructs meant walking the same trivia over and over.
        bool Unsafe(SyntaxNode node) => context.IsUnsafe(node) || node.ContainsDirectives;

        int SingleLineWidth(SyntaxNode node)
        {
            var width = 0;
            SyntaxToken previous = default;
            foreach (var token in node.DescendantTokens())
            {
                if (previous.RawKind != 0 && (HasWhitespace(previous.TrailingTrivia) || HasWhitespace(token.LeadingTrivia)))
                    width++;
                width = AddVisualWidth(width, token.Text);
                previous = token;
            }

            return width;
        }

        int VisualStartColumn(SyntaxNode node)
        {
            var text = node.SyntaxTree.GetText();
            var line = text.Lines.GetLineFromPosition(node.SpanStart);
            return AddVisualWidth(0, text.ToString(Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(line.Start, node.SpanStart)));
        }

        int AddVisualWidth(int width, string text)
        {
            foreach (var character in text)
                width = character == '\t' ? width + context.TabWidth - width % context.TabWidth : width + 1;
            return width;
        }

        static bool HasLineComment(SyntaxNode node) => node.DescendantTrivia(descendIntoTrivia: true)
            .Any(x => x.IsKind(SyntaxKind.SingleLineCommentTrivia) || x.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia));

        static bool HasWhitespace(SyntaxTriviaList trivia) => trivia.Any(x => x.ToString().Any(char.IsWhiteSpace));

        string Indent(SyntaxNode node, int extra)
        {
            var line = node.SyntaxTree.GetText().Lines.GetLineFromPosition(node.SpanStart);
            var text = line.ToString();
            var leading = new string(text.TakeWhile(char.IsWhiteSpace).ToArray());
            return leading + string.Concat(Enumerable.Repeat(context.IndentUnit, extra));
        }

        SyntaxTriviaList Line(string indent) => SyntaxFactory.TriviaList(
            SyntaxFactory.EndOfLine(context.LineEnding),
            SyntaxFactory.Whitespace(indent));

        static SyntaxTriviaList Leading(SyntaxTriviaList original, SyntaxTriviaList whitespace)
        {
            var significant = WithoutWhitespace(original);
            if (significant.Count == 0)
                return whitespace;
            return significant[^1].IsKind(SyntaxKind.SingleLineCommentTrivia)
                ? whitespace.AddRange(significant).AddRange(whitespace)
                : whitespace.AddRange(significant).Add(SyntaxFactory.Space);
        }

        static SyntaxTriviaList Trailing(SyntaxTriviaList original, SyntaxTriviaList whitespace)
        {
            var significant = WithoutWhitespace(original);
            if (significant.Count == 0)
                return whitespace;
            return significant[^1].IsKind(SyntaxKind.SingleLineCommentTrivia)
                ? whitespace.AddRange(significant).AddRange(whitespace)
                : whitespace.AddRange(significant).Add(SyntaxFactory.Space);
        }

        static SyntaxTriviaList WithoutWhitespace(SyntaxTriviaList trivia) => SyntaxFactory.TriviaList(
            trivia.Where(x => !x.IsKind(SyntaxKind.WhitespaceTrivia) && !x.IsKind(SyntaxKind.EndOfLineTrivia)));
    }
}


/// <summary>
/// Applies a run of construct-layout rules in a single walk.
/// </summary>
/// <remarks>
/// The rules own disjoint constructs and each rewrites only the whitespace inside the construct it
/// owns, so one traversal can carry them all instead of one traversal and one rebuild per rule.
/// </remarks>
static class ConstructLayoutBatch
{
    internal static SyntaxNode Apply(
        SyntaxNode root,
        ReadOnlySpan<(ConstructLayoutRule Rule, string Preference)> rules,
        RuleContext context)
    {
        var byKind = new ConstructLayoutRule.Setting?[Enum.GetValues<ConstructLayoutKind>().Length];
        foreach (var (rule, preference) in rules)
            byKind[(int)rule.Kind] = ConstructLayoutRule.Setting.For(preference, context);
        return new ConstructLayoutRule.Rewriter(byKind, context).Visit(root)!;
    }
}

enum ConstructLayoutKind
{
    Arguments,
    Parameters,
    Initializers,
    CollectionExpressions,
    BaseTypeLists,
    ConstraintClauses,
    MemberAccessChains,
    BinaryExpressions,
    ConditionalExpressions,
    QueryClauses,
    Attributes
}