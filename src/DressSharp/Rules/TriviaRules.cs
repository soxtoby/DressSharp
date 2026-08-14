using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.RegularExpressions;

namespace DressSharp.Rules;

sealed class BlankLineRule(string key, BlankLineKind kind, int order) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new(
        key,
        Enumerable.Range(0, 10)
            .Select(i => i.ToString())
            .ToImmutableArray(),
        "line breaks at structural boundaries",
        RuleSafetyClass.Layout,
        "Only whitespace trivia changes",
        order);

    public SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context)
    {
        var count = int.Parse(preference);
        if (kind == BlankLineKind.Maximum)
            return root.ReplaceTokens(
                root.DescendantTokens(descendIntoTrivia: true),
                (token, _) => token.WithLeadingTrivia(Cap(token.LeadingTrivia, Math.Max(0, count + 1 - EndingLines(token.GetPreviousToken().TrailingTrivia)))));

        var targets = new List<SyntaxNode>();
        switch (kind)
        {
            case BlankLineKind.AroundNamespaces:
            {
                targets.AddRange(root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>());
                break;
            }
            case BlankLineKind.AroundTypes:
            {
                targets.AddRange(root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>());
                break;
            }
            case BlankLineKind.BetweenMembers or BlankLineKind.BetweenMemberCategories:
            {
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                    for (var i = 1; i < type.Members.Count; i++)
                        if (kind == BlankLineKind.BetweenMembers || Category(type.Members[i - 1]) != Category(type.Members[i]))
                            targets.Add(type.Members[i]);
                break;
            }
            case BlankLineKind.BetweenUsingGroups:
            {
                foreach (var list in UsingLists(root))
                    for (var i = 1; i < list.Count; i++)
                        if (UsingCategory(list[i - 1]) != UsingCategory(list[i]))
                            targets.Add(list[i]);
                break;
            }
        }

        return root.ReplaceTokens(
            targets
                .Where(node => !context.IsUnsafe(node))
                .Select(node => node.GetFirstToken()),
            (token, _) => token.WithLeadingTrivia(SetBreaks(token.LeadingTrivia, Math.Max(0, count + 1 - EndingLines(token.GetPreviousToken().TrailingTrivia)))));
    }

    static IEnumerable<SyntaxList<UsingDirectiveSyntax>> UsingLists(SyntaxNode root)
    {
        yield return ((CompilationUnitSyntax)root).Usings;
        foreach (var ns in root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
            yield return ns.Usings;
    }

    static int UsingCategory(UsingDirectiveSyntax directive) =>
        directive.Alias is not null
            ? 2
            : directive.StaticKeyword.IsKind(SyntaxKind.StaticKeyword)
                ? 1
                : 0;

    static int Category(MemberDeclarationSyntax member) =>
        member switch
            {
                FieldDeclarationSyntax or EventFieldDeclarationSyntax => 0,
                ConstructorDeclarationSyntax => 1,
                PropertyDeclarationSyntax or IndexerDeclarationSyntax or EventDeclarationSyntax => 2,
                MethodDeclarationSyntax or OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax => 3,
                BaseTypeDeclarationSyntax or DelegateDeclarationSyntax => 4,
                _ => 5
            };

    static SyntaxTriviaList SetBreaks(SyntaxTriviaList trivia, int lines)
    {
        return trivia.Any(t => !t.IsWhitespaceOrEndOfLine())
            ? trivia
            : SyntaxFactory.TriviaList(
                Enumerable.Repeat(SyntaxFactory.ElasticCarriageReturnLineFeed, lines)
                    .Concat([SyntaxFactory.Whitespace(Indent(trivia))]));
    }

    static int EndingLines(SyntaxTriviaList trivia)
    {
        var count = 0;
        for (var i = trivia.Count - 1; i >= 0 && trivia[i].IsWhitespaceOrEndOfLine(); i--)
            if (trivia[i].IsKind(SyntaxKind.EndOfLineTrivia))
                count++;
        return count;
    }

    static SyntaxTriviaList Cap(SyntaxTriviaList trivia, int allowedLines)
    {
        var result = new List<SyntaxTrivia>();
        var lines = 0;
        foreach (var item in trivia)
        {
            if (item.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                lines++;
                if (lines > allowedLines)
                    continue;
            }
            else if (!item.IsKind(SyntaxKind.WhitespaceTrivia))
            {
                lines = 0;
            }

            result.Add(item);
        }

        return SyntaxFactory.TriviaList(result);
    }

    static string Indent(SyntaxTriviaList trivia) => trivia.LastOrDefault(t => t.IsKind(SyntaxKind.WhitespaceTrivia)).ToString();
}

enum BlankLineKind
{
    AroundNamespaces,
    AroundTypes,
    BetweenMembers,
    BetweenUsingGroups,
    BetweenMemberCategories,
    Maximum
}

enum CommentKind
{
    LineSpacing,
    BlockSpacing,
    AttachedPlacement,
    XmlPlacement,
    XmlElementLayout
}

sealed partial class CommentRule(string key, CommentKind kind, ImmutableArray<string> values, int order) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new(
        key,
        values,
        "comment-adjacent trivia",
        RuleSafetyClass.Layout,
        "Comment text remains unchanged",
        order);

    public SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context) => root.ReplaceTokens(
        root.DescendantTokens(),
        (token, _) => kind == CommentKind.XmlElementLayout
            ? token
                .WithLeadingTrivia(XmlLayout(token.LeadingTrivia, preference))
                .WithTrailingTrivia(XmlLayout(token.TrailingTrivia, preference))
            : token
                .WithLeadingTrivia(Edit(token.LeadingTrivia, preference))
                .WithTrailingTrivia(Edit(token.TrailingTrivia, preference)));

    SyntaxTriviaList Edit(SyntaxTriviaList trivia, string preference)
    {
        var items = trivia.ToList();
        for (var i = 0; i < items.Count - 1; i++)
        {
            var line = items[i].IsKind(SyntaxKind.SingleLineCommentTrivia);
            var block = items[i].IsKind(SyntaxKind.MultiLineCommentTrivia);
            if (kind == CommentKind.AttachedPlacement && (line || block) && preference != "auto")
            {
                var own = preference == "own_line" || line;
                if (items[i + 1].IsKind(SyntaxKind.WhitespaceTrivia) || items[i + 1].IsKind(SyntaxKind.EndOfLineTrivia))
                    items[i + 1] = own ? SyntaxFactory.ElasticCarriageReturnLineFeed : SyntaxFactory.Space;
            }
        }

        if (kind == CommentKind.XmlPlacement)
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].HasStructure && items[i].GetStructure() is DocumentationCommentTriviaSyntax)
                {
                    var start = i + 1;
                    var end = start;
                    while (end < items.Count && items[end].IsWhitespaceOrEndOfLine())
                        end++;
                    var indent = items
                        .Skip(start)
                        .Take(end - start)
                        .LastOrDefault(t => t.IsKind(SyntaxKind.WhitespaceTrivia))
                        .ToString();
                    items.RemoveRange(start, end - start);
                    items.InsertRange(
                        start,
                        Enumerable.Repeat(SyntaxFactory.ElasticCarriageReturnLineFeed, preference == "attached" ? 0 : 1)
                            .Append(SyntaxFactory.Whitespace(indent)));
                }
            }
        }

        for (var i = 0; i < items.Count; i++)
        {
            switch (kind)
            {
                case CommentKind.LineSpacing when items[i].IsKind(SyntaxKind.SingleLineCommentTrivia):
                {
                    var text = items[i].ToString();
                    items[i] = SyntaxFactory.Comment("//" + (preference == "single" ? " " : "") + text[2..].TrimStart());
                    break;
                }
                case CommentKind.BlockSpacing when items[i].IsKind(SyntaxKind.MultiLineCommentTrivia):
                {
                    var text = items[i].ToString();
                    var content = text[2..^2].Trim();
                    items[i] = SyntaxFactory.Comment("/*" + (preference == "single" ? " " : "") + content + (preference == "single" ? " " : "") + "*/");
                    break;
                }
            }
        }

        return SyntaxFactory.TriviaList(items);
    }

    static SyntaxTriviaList XmlLayout(SyntaxTriviaList trivia, string preference)
    {
        if (trivia.None(t => t.HasStructure && t.GetStructure() is DocumentationCommentTriviaSyntax))
            return trivia;
        var text = trivia.ToFullString();
        text = preference == "multi_line"
            ? SingleLineXmlElementPattern().Replace(text, m => $"{m.Groups[1].Value}<{m.Groups[2].Value}>\n{m.Groups[1].Value}{m.Groups[3].Value.Trim()}\n{m.Groups[1].Value}</{m.Groups[2].Value}>")
            : MultiLineXmlElementPattern().Replace(text, m => $"{m.Groups[1].Value}<{m.Groups[2].Value}>{m.Groups[3].Value.Trim()}</{m.Groups[2].Value}>");
        return SyntaxFactory.ParseLeadingTrivia(text);
    }

    [GeneratedRegex(@"^(\s*///\s*)<([A-Za-z][\w.-]*)>([^<\r\n]+)</\2>\s*$", RegexOptions.Multiline)]
    private static partial Regex SingleLineXmlElementPattern();

    [GeneratedRegex(@"^(\s*///\s*)<([A-Za-z][\w.-]*)>\s*\r?\n\s*///\s*([^<\r\n]+)\s*\r?\n\s*///\s*</\2>\s*$", RegexOptions.Multiline)]
    private static partial Regex MultiLineXmlElementPattern();
}
