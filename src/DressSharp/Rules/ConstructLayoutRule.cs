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

    public SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context)
    {
        var maximum = int.MaxValue;
        if (preference.Equals("auto", StringComparison.OrdinalIgnoreCase))
            maximum = context.MaximumLineLength;

        return new Rewriter(kind, preference, maximum, context).Visit(root)!;
    }

    sealed class Rewriter(ConstructLayoutKind kind, string preference, int maximum, RuleContext context) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitArgumentList(ArgumentListSyntax node) =>
            kind == ConstructLayoutKind.Arguments
                ? Delimited((ArgumentListSyntax)base.VisitArgumentList(node)!)
                : base.VisitArgumentList(node);

        public override SyntaxNode? VisitBracketedArgumentList(BracketedArgumentListSyntax node) =>
            kind == ConstructLayoutKind.Arguments
                ? Delimited((BracketedArgumentListSyntax)base.VisitBracketedArgumentList(node)!)
                : base.VisitBracketedArgumentList(node);

        public override SyntaxNode? VisitParameterList(ParameterListSyntax node) =>
            kind == ConstructLayoutKind.Parameters
                ? Delimited((ParameterListSyntax)base.VisitParameterList(node)!)
                : base.VisitParameterList(node);

        public override SyntaxNode? VisitBracketedParameterList(BracketedParameterListSyntax node) =>
            kind == ConstructLayoutKind.Parameters
                ? Delimited((BracketedParameterListSyntax)base.VisitBracketedParameterList(node)!)
                : base.VisitBracketedParameterList(node);

        public override SyntaxNode? VisitInitializerExpression(InitializerExpressionSyntax node) =>
            kind == ConstructLayoutKind.Initializers
                ? Delimited((InitializerExpressionSyntax)base.VisitInitializerExpression(node)!)
                : base.VisitInitializerExpression(node);

        public override SyntaxNode? VisitCollectionExpression(CollectionExpressionSyntax node) =>
            kind == ConstructLayoutKind.CollectionExpressions
                ? Delimited((CollectionExpressionSyntax)base.VisitCollectionExpression(node)!)
                : base.VisitCollectionExpression(node);

        public override SyntaxNode? VisitBaseList(BaseListSyntax node)
        {
            var visited = (BaseListSyntax)base.VisitBaseList(node)!;
            if (kind != ConstructLayoutKind.BaseTypeLists || visited.Types.Count == 0 || Unsafe(visited))
                return visited;
            return LayoutSeparated(visited, visited.Types.GetSeparators(), visited.Types.Select(x => x.GetFirstToken()));
        }

        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node) => Constraints((ClassDeclarationSyntax)base.VisitClassDeclaration(node)!);
        public override SyntaxNode VisitStructDeclaration(StructDeclarationSyntax node) => Constraints((StructDeclarationSyntax)base.VisitStructDeclaration(node)!);
        public override SyntaxNode VisitInterfaceDeclaration(InterfaceDeclarationSyntax node) => Constraints((InterfaceDeclarationSyntax)base.VisitInterfaceDeclaration(node)!);
        public override SyntaxNode VisitRecordDeclaration(RecordDeclarationSyntax node) => Constraints((RecordDeclarationSyntax)base.VisitRecordDeclaration(node)!);

        T Constraints<T>(T visited) where T : TypeDeclarationSyntax
        {
            if (kind != ConstructLayoutKind.ConstraintClauses || visited.ConstraintClauses.Count == 0 || Unsafe(visited))
                return visited;
            return LayoutItems(visited, visited.ConstraintClauses.Select(x => x.GetFirstToken()), continuation: true);
        }

        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var visited = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;
            if (kind != ConstructLayoutKind.ConstraintClauses || visited.ConstraintClauses.Count == 0 || Unsafe(visited))
                return visited;
            return LayoutItems(visited, visited.ConstraintClauses.Select(x => x.GetFirstToken()), continuation: true);
        }

        public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            var visited = (LocalFunctionStatementSyntax)base.VisitLocalFunctionStatement(node)!;
            if (kind != ConstructLayoutKind.ConstraintClauses || visited.ConstraintClauses.Count == 0 || Unsafe(visited))
                return visited;
            return LayoutItems(visited, visited.ConstraintClauses.Select(x => x.GetFirstToken()), continuation: true);
        }

        public override SyntaxNode? VisitDelegateDeclaration(DelegateDeclarationSyntax node)
        {
            var visited = (DelegateDeclarationSyntax)base.VisitDelegateDeclaration(node)!;
            if (kind != ConstructLayoutKind.ConstraintClauses || visited.ConstraintClauses.Count == 0 || Unsafe(visited))
                return visited;
            return LayoutItems(visited, visited.ConstraintClauses.Select(x => x.GetFirstToken()), continuation: true);
        }

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var visited = (MemberAccessExpressionSyntax)base.VisitMemberAccessExpression(node)!;
            if (kind != ConstructLayoutKind.MemberAccessChains || node.Parent is MemberAccessExpressionSyntax || Unsafe(visited))
                return visited;
            var operators = visited.DescendantTokens().Where(x => x.IsKind(SyntaxKind.DotToken) || x.IsKind(SyntaxKind.MinusGreaterThanToken));
            return LayoutItems(visited, operators, continuation: true, compactSingle: true);
        }

        public override SyntaxNode? VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            var visited = (BinaryExpressionSyntax)base.VisitBinaryExpression(node)!;
            if (kind != ConstructLayoutKind.BinaryExpressions || node.Parent is BinaryExpressionSyntax || Unsafe(visited))
                return visited;
            var operators = visited.DescendantNodesAndSelf().OfType<BinaryExpressionSyntax>().Select(x => x.OperatorToken);
            return LayoutItems(visited, operators, continuation: true);
        }

        public override SyntaxNode? VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
            var visited = (ConditionalExpressionSyntax)base.VisitConditionalExpression(node)!;
            if (kind != ConstructLayoutKind.ConditionalExpressions || node.Parent is ConditionalExpressionSyntax || Unsafe(visited))
                return visited;
            return LayoutItems(visited, [visited.QuestionToken, visited.ColonToken], continuation: true);
        }

        public override SyntaxNode? VisitQueryExpression(QueryExpressionSyntax node)
        {
            var visited = (QueryExpressionSyntax)base.VisitQueryExpression(node)!;
            if (kind != ConstructLayoutKind.QueryClauses || Unsafe(visited))
                return visited;
            var starts = visited.Body.Clauses.Select(x => x.GetFirstToken()).Append(visited.Body.SelectOrGroup.GetFirstToken());
            return LayoutItems(visited, starts, continuation: true);
        }

        public override SyntaxNode? VisitAttributeList(AttributeListSyntax node) =>
            kind == ConstructLayoutKind.Attributes ? Delimited((AttributeListSyntax)base.VisitAttributeList(node)!) : base.VisitAttributeList(node);

        T Delimited<T>(T node) where T : SyntaxNode
        {
            if (Unsafe(node))
                return node;
            var open = node.GetFirstToken();
            var close = node.GetLastToken();
            var separators = DirectSeparators(node).ToArray();
            var elements = DirectElements(node).ToArray();
            if (elements.Length == 0)
                return node;

            var multi = IsMulti(node);
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

        T LayoutSeparated<T>(T node, IEnumerable<SyntaxToken> separators, IEnumerable<SyntaxToken> starts) where T : SyntaxNode
        {
            var multi = IsMulti(node);
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

        T LayoutItems<T>(T node, IEnumerable<SyntaxToken> starts, bool continuation, bool compactSingle = false) where T : SyntaxNode
        {
            var tokens = starts.Distinct().ToArray();
            if (tokens.Length == 0)
                return node;
            
            var multi = IsMulti(node);
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

        bool IsMulti(SyntaxNode node)
        {
            if (preference.Equals("always_multi", StringComparison.OrdinalIgnoreCase))
                return true;
            if (preference.Equals("always_single", StringComparison.OrdinalIgnoreCase) || maximum == int.MaxValue)
                return false;
            return VisualStartColumn(node) + SingleLineWidth(node) > maximum;
        }

        bool Unsafe(SyntaxNode node) => context.IsUnsafe(node) || node.DescendantTrivia(descendIntoTrivia: true).Any(x => x.IsDirective);

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