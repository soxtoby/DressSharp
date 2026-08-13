using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DressSharp.Rules;

enum InitializerKind
{
    Object,
    Collection,
    Array,
    With,
    CollectionExpression
}

sealed class InitializerIndentationRule(string key, InitializerKind kind, int order) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new(key, ["indented", "not_indented"], "multiline non-empty initializer delimiters", RuleSafetyClass.Layout, "Only delimiter indentation changes", order);

    public SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context)
    {
        var pairs = root.DescendantNodes()
            .Select(Pair)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Where(x => x.Open.Span.End < x.Close.SpanStart
                && (root.SyntaxTree.GetLineSpan(TextSpan.FromBounds(x.Open.SpanStart, x.Close.Span.End)).StartLinePosition.Line
                    != root.SyntaxTree.GetLineSpan(TextSpan.FromBounds(x.Open.SpanStart, x.Close.Span.End)).EndLinePosition.Line)
                && !context.IsUnsafe(TextSpan.FromBounds(x.Open.SpanStart, x.Close.Span.End)))
            .ToArray();
        var text = root.SyntaxTree.GetText();
        var replacements = pairs.SelectMany(x =>
            {
                var desired = BaseIndent(text, x.Open) + (preference == "indented" ? "    " : "");
                return new[] { (x.Open, desired), (x.Close, desired) };
            }).ToDictionary(x => x.Item1, x => x.desired);
        return root.ReplaceTokens(replacements.Keys, (token, _) => token.WithLeadingTrivia(Indent(token.LeadingTrivia, replacements[token])));
    }

    (SyntaxToken Open, SyntaxToken Close)? Pair(SyntaxNode n) => n switch
        {
            InitializerExpressionSyntax { Expressions.Count: > 0 } x when Matches(x) =>
                (x.OpenBraceToken, x.CloseBraceToken),
            CollectionExpressionSyntax x when kind == InitializerKind.CollectionExpression && x.Elements.Count > 0 =>
                (x.OpenBracketToken, x.CloseBracketToken),
            _ => null
        };

    bool Matches(InitializerExpressionSyntax x) => kind switch
        {
            InitializerKind.Object => x.IsKind(SyntaxKind.ObjectInitializerExpression),
            InitializerKind.Collection => x.IsKind(SyntaxKind.CollectionInitializerExpression),
            InitializerKind.Array => x.IsKind(SyntaxKind.ArrayInitializerExpression),
            InitializerKind.With => x.IsKind(SyntaxKind.WithInitializerExpression),
            _ => false
        };

    static string BaseIndent(SourceText text, SyntaxToken token)
    {
        var previous = token.GetPreviousToken();
        var line = text.Lines.GetLineFromPosition(previous.SpanStart).ToString();
        return new string(line.TakeWhile(char.IsWhiteSpace).ToArray());
    }

    static SyntaxTriviaList Indent(SyntaxTriviaList trivia, string desired)
    {
        var items = trivia.ToList();
        var eol = items.FindLastIndex(x => x.IsKind(SyntaxKind.EndOfLineTrivia));
        var index = eol + 1;
        if (index < items.Count && items[index].IsKind(SyntaxKind.WhitespaceTrivia))
        {
            items[index] = SyntaxFactory.Whitespace(desired);
        }
        else if (items.All(x => x.IsKind(SyntaxKind.WhitespaceTrivia)))
        {
            items.Clear();
            items.Add(SyntaxFactory.Whitespace(desired));
        }
        else
        {
            items.Insert(index, SyntaxFactory.Whitespace(desired));
        }

        return SyntaxFactory.TriviaList(items);
    }
}