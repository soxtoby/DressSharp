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

    internal BlankLineKind Kind => kind;

    public SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context) =>
        BlankLineBatch.Apply(root, [new(this, preference)], context);

    /// <summary>
    /// The first token of every construct this rule owns a line break in front of.
    /// </summary>
    internal IEnumerable<SyntaxToken> Targets(SyntaxNode root, RuleContext context)
    {
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

        return targets
            .Where(node => !context.IsUnsafe(node))
            .Select(node => node.GetFirstToken());
    }

    /// <summary>
    /// The leading trivia a target token should carry to sit behind <paramref name="count"/> blank
    /// lines, given whatever leading trivia it carries now.
    /// </summary>
    internal static SyntaxTriviaList Separate(SyntaxToken token, SyntaxTriviaList leading, int count) =>
        SetBreaks(leading, Math.Max(0, count + 1 - EndingLines(token.GetPreviousToken().TrailingTrivia)));

    /// <summary>
    /// The leading trivia a token should carry once runs of blank lines are capped at
    /// <paramref name="count"/>.
    /// </summary>
    internal static SyntaxTriviaList CapRuns(SyntaxToken token, SyntaxTriviaList leading, int count) =>
        Cap(leading, Math.Max(0, count + 1 - EndingLines(token.GetPreviousToken().TrailingTrivia)));

    internal static bool ContainsEndOfLine(SyntaxTriviaList trivia)
    {
        foreach (var item in trivia)
        {
            if (item.IsKind(SyntaxKind.EndOfLineTrivia))
                return true;
        }

        return false;
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

/// <summary>
/// Applies a run of blank-line rules in a single walk.
/// </summary>
/// <remarks>
/// Each rule in the run only ever rewrites the leading trivia of tokens it owns, and reads only that
/// leading trivia plus the trailing trivia of the preceding token, which no rule in the run touches.
/// So the rules compose: gathering their edits against one snapshot and applying them together gives
/// what running them in sequence gives, for one rebuild of the tree rather than one per rule.
/// </remarks>
static class BlankLineBatch
{
    internal static SyntaxNode Apply(
        SyntaxNode root,
        ReadOnlySpan<(BlankLineRule Rule, string Preference)> rules,
        RuleContext context)
    {
        var separated = new Dictionary<SyntaxToken, SyntaxTriviaList>();
        int? cap = null;
        foreach (var (rule, preference) in rules)
        {
            var count = int.Parse(preference);
            if (rule.Kind == BlankLineKind.Maximum)
            {
                // Maximum runs last in catalog order, so it caps whatever the others produced.
                cap = count;
                continue;
            }

            foreach (var token in rule.Targets(root, context))
            {
                var leading = separated.TryGetValue(token, out var pending) ? pending : token.LeadingTrivia;
                separated[token] = BlankLineRule.Separate(token, leading, count);
            }
        }

        return separated.Count == 0 && cap is null
            ? root
            : new Rewriter(separated, cap).Visit(root)!;
    }

    /// <summary>
    /// Whether this run can share a pass, which needs the capping rule to come last so that it sees
    /// the other rules' output, exactly as it would when the rules run one at a time.
    /// </summary>
    internal static bool Composes(List<(BlankLineRule Rule, string Preference)> rules)
    {
        for (var index = 0; index < rules.Count - 1; index++)
        {
            if (rules[index].Rule.Kind == BlankLineKind.Maximum)
                return false;
        }

        return true;
    }

    sealed class Rewriter(Dictionary<SyntaxToken, SyntaxTriviaList> separated, int? cap)
        : CSharpSyntaxRewriter(visitIntoStructuredTrivia: cap is not null)
    {
        public override SyntaxToken VisitToken(SyntaxToken token)
        {
            var leading = token.LeadingTrivia;
            var edited = false;
            if (separated.Count > 0 && separated.TryGetValue(token, out var replacement))
            {
                leading = replacement;
                edited = true;
            }

            // Only leading trivia holding a line break can be capped, and most tokens carry none, so
            // this avoids the previous-token lookup and the list rebuild for the great majority.
            if (cap is { } allowed && BlankLineRule.ContainsEndOfLine(leading))
            {
                leading = BlankLineRule.CapRuns(token, leading, allowed);
                edited = true;
            }

            return base.VisitToken(edited ? token.WithLeadingTrivia(leading) : token);
        }
    }
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

    public SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context) =>
        CommentRuleBatch.Apply(root, [new(this, preference)]);

    /// <summary>
    /// Rewrites one trivia list. The rule's whole effect is this function, applied to the leading and
    /// trailing trivia of every token, which is what lets a run of comment rules share a single walk.
    /// </summary>
    internal SyntaxTriviaList Apply(SyntaxTriviaList trivia, string preference) =>
        kind == CommentKind.XmlElementLayout
            ? XmlLayout(trivia, preference)
            : Edit(trivia, preference);

    /// <summary>
    /// Whether this trivia list holds anything the rule's kind acts on. Most tokens carry no comment
    /// at all, and the edit below rebuilds the list unconditionally, so this check runs first.
    /// </summary>
    bool Relevant(SyntaxTriviaList trivia)
    {
        return trivia.Any(item => kind switch
            {
                CommentKind.LineSpacing => item.IsKind(SyntaxKind.SingleLineCommentTrivia),
                CommentKind.BlockSpacing => item.IsKind(SyntaxKind.MultiLineCommentTrivia),
                CommentKind.AttachedPlacement => item.IsKind(SyntaxKind.SingleLineCommentTrivia) || item.IsKind(SyntaxKind.MultiLineCommentTrivia),
                CommentKind.XmlPlacement => item.HasStructure && item.GetStructure() is DocumentationCommentTriviaSyntax,
                _ => false
            });
    }

    SyntaxTriviaList Edit(SyntaxTriviaList trivia, string preference)
    {
        if (!Relevant(trivia))
            return trivia;

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
        if (!ContainsDocumentationComment(trivia))
            return trivia;
        var text = trivia.ToFullString();
        text = preference == "multi_line"
            ? SingleLineXmlElementPattern().Replace(text, m => $"{m.Groups[1].Value}<{m.Groups[2].Value}>\n{m.Groups[1].Value}{m.Groups[3].Value.Trim()}\n{m.Groups[1].Value}</{m.Groups[2].Value}>")
            : MultiLineXmlElementPattern().Replace(text, m => $"{m.Groups[1].Value}<{m.Groups[2].Value}>{m.Groups[3].Value.Trim()}</{m.Groups[2].Value}>");
        return SyntaxFactory.ParseLeadingTrivia(text);
    }

    static bool ContainsDocumentationComment(SyntaxTriviaList trivia) => 
        trivia.Any(item => item.HasStructure && item.GetStructure() is DocumentationCommentTriviaSyntax);

    [GeneratedRegex(@"^(\s*///\s*)<([A-Za-z][\w.-]*)>([^<\r\n]+)</\2>\s*$", RegexOptions.Multiline)]
    private static partial Regex SingleLineXmlElementPattern();

    [GeneratedRegex(@"^(\s*///\s*)<([A-Za-z][\w.-]*)>\s*\r?\n\s*///\s*([^<\r\n]+)\s*\r?\n\s*///\s*</\2>\s*$", RegexOptions.Multiline)]
    private static partial Regex MultiLineXmlElementPattern();
}

/// <summary>
/// Applies a run of comment rules in a single walk.
/// </summary>
/// <remarks>
/// Each comment rule rewrites a token's leading and trailing trivia and nothing else, so applying
/// them one after another to the same trivia list gives exactly what running them as separate passes
/// would, for the cost of one walk and one rebuild rather than one of each per rule.
/// </remarks>
static class CommentRuleBatch
{
    internal static SyntaxNode Apply(SyntaxNode root, ReadOnlySpan<(CommentRule Rule, string Preference)> rules) =>
        new Rewriter(rules.ToArray()).Visit(root)!;

    sealed class Rewriter((CommentRule Rule, string Preference)[] rules) : CSharpSyntaxRewriter
    {
        public override SyntaxToken VisitToken(SyntaxToken token)
        {
            var leading = token.LeadingTrivia;
            var trailing = token.TrailingTrivia;
            foreach (var (rule, preference) in rules)
            {
                leading = rule.Apply(leading, preference);
                trailing = rule.Apply(trailing, preference);
            }

            // Leaving an untouched token alone keeps its whole ancestor spine shared, so a file with
            // no comments falls straight through instead of being rebuilt once per comment rule.
            if (leading.Equals(token.LeadingTrivia) && trailing.Equals(token.TrailingTrivia))
                return token;
            return token.WithLeadingTrivia(leading).WithTrailingTrivia(trailing);
        }
    }
}