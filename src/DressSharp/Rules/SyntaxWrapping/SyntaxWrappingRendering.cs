using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DressSharp.Rules;

static class SyntaxWrappingRendering
{
    internal static bool ContainsLineComment(SyntaxTriviaList trivia)
    {
        return trivia.Any(item =>
            item.IsKind(SyntaxKind.SingleLineCommentTrivia)
            || item.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia));
    }

    internal readonly record struct Boundary(int RightIndex, GapStyle Style, int OperatorIndex = -1, bool BreakWhenMulti = true);

    internal enum GapStyle
    {
        DelimitedFirst,
        DelimitedSpacedFirst,
        DelimitedLater,
        DelimitedClose,
        DelimitedSpacedClose,
        InitializerOpen,
        SeparatedFirst,
        SeparatedLater,
        Item,
        CompactItem
    }

    internal readonly record struct Occurrence(
        SyntaxNode Node,
        SyntaxWrappingKind Kind,
        SyntaxWrappingSettings.Setting Setting,
        int BoundaryStart,
        int BoundaryCount,
        int Parent,
        int FirstToken,
        int LastToken,
        bool Applies = false,
        bool Multi = false);

    internal static int BaseGapWidth(
        SyntaxToken left,
        SyntaxToken right,
        SyntaxTriviaList leftTrailing,
        SyntaxTriviaList rightLeading,
        int tabWidth,
        EmitterPlan emitterPlan)
    {
        if (HasSignificantTrivia(leftTrailing) || HasSignificantTrivia(rightLeading))
        {
            var text = leftTrailing.ToFullString() + rightLeading.ToFullString();
            return CollapsedWidth(text, tabWidth);
        }

        var candidates = emitterPlan.Trigger(left.RawKind) | emitterPlan.Trigger(right.RawKind);
        var desired = (candidates == 0 ? null : emitterPlan.DesiredSpace(left, right, candidates))
            ?? leftTrailing.Count != 0
            || rightLeading.Count != 0;
        return desired ? 1 : 0;
    }

    internal static string Render(
        Boundary boundary,
        TriviaLayoutPlan trivia,
        bool multi,
        string indent,
        string baseIndent,
        string lineEnding)
    {
        var leftTrailing = trivia.Trailing(boundary.RightIndex - 1);
        var rightLeading = trivia.Leading(boundary.RightIndex);
        if (!HasSignificantTrivia(leftTrailing)
            && !HasSignificantTrivia(rightLeading))
        {
            if (multi)
            {
                return lineEnding + (boundary.Style is
                    GapStyle.DelimitedClose
                    or GapStyle.DelimitedSpacedClose
                    or GapStyle.InitializerOpen
                        ? baseIndent
                        : indent);
            }
            return WantsSingleSpace(boundary.Style) ? " " : "";
        }

        var line = SyntaxFactory.TriviaList(
            SyntaxFactory.EndOfLine(lineEnding),
            SyntaxFactory.Whitespace(indent));
        var baseLine = SyntaxFactory.TriviaList(
            SyntaxFactory.EndOfLine(lineEnding),
            SyntaxFactory.Whitespace(baseIndent));
        var space = SyntaxFactory.TriviaList(SyntaxFactory.Space);
        SyntaxTriviaList left;
        SyntaxTriviaList right;
        switch (boundary.Style)
        {
            case GapStyle.DelimitedFirst:
                left = Trailing(leftTrailing, multi ? line : default);
                right = Leading(rightLeading, default);
                break;
            case GapStyle.DelimitedSpacedFirst:
                left = Trailing(leftTrailing, multi ? line : space);
                right = Leading(rightLeading, default);
                break;
            case GapStyle.DelimitedLater:
                left = Trailing(leftTrailing, multi ? line : space);
                right = Leading(rightLeading, default);
                break;
            case GapStyle.DelimitedClose:
                left = WithoutWhitespace(leftTrailing);
                right = Leading(rightLeading, multi ? baseLine : default);
                break;
            case GapStyle.DelimitedSpacedClose:
                left = WithoutWhitespace(leftTrailing);
                right = Leading(rightLeading, multi ? baseLine : space);
                break;
            case GapStyle.InitializerOpen:
                left = Trailing(leftTrailing, multi ? baseLine : space);
                right = Leading(rightLeading, default);
                break;
            case GapStyle.SeparatedFirst:
                left = WithoutWhitespace(leftTrailing);
                right = Leading(rightLeading, multi ? line : space);
                break;
            case GapStyle.SeparatedLater:
                left = Trailing(leftTrailing, multi ? default : space);
                right = Leading(rightLeading, multi ? line : default);
                break;
            case GapStyle.CompactItem:
                left = WithoutWhitespace(leftTrailing);
                right = Leading(rightLeading, multi ? line : default);
                break;
            default:
                left = WithoutWhitespace(leftTrailing);
                right = Leading(rightLeading, multi ? line : space);
                break;
        }

        return left.ToFullString() + right.ToFullString();
    }

    internal static int PlannedWidth(
        Boundary boundary,
        TriviaLayoutPlan trivia,
        bool multi,
        string lineEnding,
        int tabWidth)
    {
        var leftTrailing = trivia.Trailing(boundary.RightIndex - 1);
        var rightLeading = trivia.Leading(boundary.RightIndex);
        if (!HasSignificantTrivia(leftTrailing)
            && !HasSignificantTrivia(rightLeading))
        {
            return multi || WantsSingleSpace(boundary.Style) ? 1 : 0;
        }

        return CollapsedWidth(Render(boundary, trivia, multi, "", "", lineEnding), tabWidth);
    }

    static bool WantsSingleSpace(GapStyle style) => style is
        GapStyle.DelimitedSpacedFirst
        or GapStyle.InitializerOpen
        or GapStyle.DelimitedLater
        or GapStyle.DelimitedSpacedClose
        or GapStyle.SeparatedFirst
        or GapStyle.SeparatedLater
        or GapStyle.Item;

    static int CollapsedWidth(string text, int tabWidth)
    {
        var width = 0;
        var pendingSpace = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = true;
                continue;
            }

            if (pendingSpace)
                width++;
            width = character == '\t'
                ? width + tabWidth - width % tabWidth
                : width + 1;
            pendingSpace = false;
        }

        return pendingSpace ? width + 1 : width;
    }

    internal static int VisualWidth(string text, int tabWidth)
    {
        if (!text.Contains('\t'))
            return text.Length;

        var width = 0;
        foreach (var character in text)
        {
            width = character == '\t'
                ? width + tabWidth - width % tabWidth
                : width + 1;
        }

        return width;
    }

    internal static int VisualWidth(
        string text,
        int start,
        int end,
        int tabWidth,
        int initialWidth)
    {
        var width = initialWidth;
        for (var position = start; position < end; position++)
        {
            width = text[position] == '\t'
                ? width + tabWidth - width % tabWidth
                : width + 1;
        }

        return width;
    }

    static bool HasSignificantTrivia(SyntaxTriviaList trivia)
    {
        foreach (var item in trivia)
        {
            if (!item.IsKind(SyntaxKind.WhitespaceTrivia)
                && !item.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                return true;
            }
        }

        return false;
    }

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
        trivia.Where(item => !item.IsKind(SyntaxKind.WhitespaceTrivia)
            && !item.IsKind(SyntaxKind.EndOfLineTrivia)));
}
