using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DressSharp.Rules;

static class SyntaxWrappingRendering
{
    internal static bool ContainsLineComment(SyntaxTriviaList trivia)
    {
        // Indexed rather than LINQ: the solver asks this for every token of every occurrence it
        // measures, and the list's interface enumerator boxes on each call.
        for (var index = 0; index < trivia.Count; index++)
        {
            if (trivia[index].IsKind(SyntaxKind.SingleLineCommentTrivia)
                || trivia[index].IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia))
            {
                return true;
            }
        }

        return false;
    }

    internal readonly record struct Boundary(
        int RightIndex,
        GapStyle Style,
        int OperatorIndex = -1,
        bool BreakWhenMulti = true,
        string? SingleLineGap = null);

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
        if (!multi && boundary.SingleLineGap is { } singleLineGap)
            return singleLineGap;
        var leftTrailing = trivia.Trailing(boundary.RightIndex - 1);
        var rightLeading = trivia.Leading(boundary.RightIndex);
        var planned = ClosingIndent(boundary.Style, indent, baseIndent);
        if (!HasSignificantTrivia(leftTrailing)
            && !HasSignificantTrivia(rightLeading))
        {
            if (multi)
                return lineEnding + planned;
            return WantsSingleSpace(boundary.Style) ? " " : "";
        }

        if (multi)
            return RenderLines(leftTrailing, rightLeading, lineEnding, planned);

        // A comment trailing the opening token keeps the gap's whitespace after it where the style
        // attaches the gap to that token; elsewhere the whitespace goes with the following token.
        var whitespace = WantsSingleSpace(boundary.Style) ? SyntaxFactory.TriviaList(SyntaxFactory.Space) : default;
        var (left, right) = boundary.Style is
            GapStyle.DelimitedFirst
            or GapStyle.DelimitedSpacedFirst
            or GapStyle.DelimitedLater
            or GapStyle.InitializerOpen
            or GapStyle.SeparatedLater
                ? (Trailing(leftTrailing, whitespace), Leading(rightLeading, default))
                : (WithoutWhitespace(leftTrailing), Leading(rightLeading, whitespace));

        // Whatever the style asked for, a gap may not end inside a line comment: the token written
        // after it would land in the comment and disappear from the file. This is the one place
        // that can see the finished gap and close the comment.
        return EndsInsideLineComment(left, right)
            ? left.ToFullString() + right.ToFullString() + lineEnding + planned
            : left.ToFullString() + right.ToFullString();
    }

    /// <summary>
    /// The gap with a line break planned before the token it leads, keeping every comment in it on
    /// the line its author gave it.
    /// </summary>
    /// <remarks>
    /// A comment on a line of its own keeps that line and the column it arrived at, which is what
    /// the emitter measures its move from. A comment on the token's line goes with the token, as does
    /// a block comment trailing the opening token with nothing between them, since the break falls
    /// before it. Blank lines are not kept, as in a gap holding no comment.
    /// </remarks>
    static string RenderLines(
        SyntaxTriviaList leftTrailing,
        SyntaxTriviaList rightLeading,
        string lineEnding,
        string indent)
    {
        List<List<SyntaxTrivia>> lines = [[]];
        foreach (var list in (ReadOnlySpan<SyntaxTriviaList>)[leftTrailing, rightLeading])
        {
            foreach (var trivia in list)
            {
                if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                    lines.Add([]);
                else
                    lines[^1].Add(trivia);
            }
        }

        var output = new StringBuilder();
        var leading = lines.Count == 1 ? Content(lines[0]) : null;
        if (lines.Count > 1)
        {
            var ownLines = false;
            for (var index = 1; index < lines.Count - 1; index++)
                ownLines |= HasContent(lines[index]);
            if (Content(lines[0]) is { } trailing)
            {
                // A line comment is the last trivia on its line, so this is the one to check.
                if (ownLines || lines[0][^1].IsKind(SyntaxKind.SingleLineCommentTrivia))
                    output.Append(' ').Append(trailing);
                else
                    leading = trailing;
            }

            for (var index = 1; index < lines.Count - 1; index++)
            {
                if (Content(lines[index]) is { } content)
                    output.Append(lineEnding).Append(Indent(lines[index])).Append(content);
            }

            if (Content(lines[^1]) is { } attached)
                leading = leading is null ? attached : leading + " " + attached;
        }

        output.Append(lineEnding).Append(indent);
        if (leading is not null)
            output.Append(leading).Append(' ');
        return output.ToString();
    }

    static bool HasContent(List<SyntaxTrivia> line) =>
        line.Exists(trivia => !trivia.IsKind(SyntaxKind.WhitespaceTrivia));

    /// <summary>The text from the first to the last comment on a line, or null when the line holds none.</summary>
    static string? Content(List<SyntaxTrivia> line)
    {
        var first = 0;
        while (first < line.Count && line[first].IsKind(SyntaxKind.WhitespaceTrivia))
            first++;
        if (first == line.Count)
            return null;

        var last = line.Count;
        while (line[last - 1].IsKind(SyntaxKind.WhitespaceTrivia))
            last--;
        var content = new StringBuilder();
        for (var index = first; index < last; index++)
            content.Append(line[index].ToFullString());
        return content.ToString();
    }

    static string Indent(List<SyntaxTrivia> line) =>
        line.Count > 0 && line[0].IsKind(SyntaxKind.WhitespaceTrivia) ? line[0].ToFullString() : "";

    static string ClosingIndent(GapStyle style, string indent, string baseIndent) =>
        style is GapStyle.DelimitedClose or GapStyle.DelimitedSpacedClose or GapStyle.InitializerOpen
            ? baseIndent
            : indent;

    static bool EndsInsideLineComment(SyntaxTriviaList left, SyntaxTriviaList right)
    {
        for (var index = right.Count - 1; index >= 0; index--)
        {
            if (right[index].IsKind(SyntaxKind.EndOfLineTrivia))
                return false;
            if (IsLineComment(right[index]))
                return true;
        }

        for (var index = left.Count - 1; index >= 0; index--)
        {
            if (left[index].IsKind(SyntaxKind.EndOfLineTrivia))
                return false;
            if (IsLineComment(left[index]))
                return true;
        }

        return false;
    }

    static bool IsLineComment(SyntaxTrivia trivia) =>
        trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
        || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia);

    internal static int PlannedWidth(
        Boundary boundary,
        TriviaLayoutPlan trivia,
        bool multi,
        string lineEnding,
        int tabWidth)
    {
        if (!multi && boundary.SingleLineGap is { } singleLineGap)
            return CollapsedWidth(singleLineGap, tabWidth);
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
        if (significant[^1].IsKind(SyntaxKind.SingleLineCommentTrivia))
        {
            // A trailing line comment belongs to the preceding item. The planned break goes
            // after it; putting that break before it makes it lead the following item instead.
            var startsOwnLine = false;
            for (var index = 0; index < original.Count
                && (original[index].IsKind(SyntaxKind.WhitespaceTrivia) || original[index].IsKind(SyntaxKind.EndOfLineTrivia)); index++)
            {
                startsOwnLine |= original[index].IsKind(SyntaxKind.EndOfLineTrivia);
            }
            return (startsOwnLine ? whitespace : SyntaxFactory.TriviaList(SyntaxFactory.Space))
                .AddRange(significant)
                .AddRange(whitespace);
        }
        return whitespace.AddRange(significant).Add(SyntaxFactory.Space);
    }

    static SyntaxTriviaList WithoutWhitespace(SyntaxTriviaList trivia) => SyntaxFactory.TriviaList(
        trivia.Where(item => !item.IsKind(SyntaxKind.WhitespaceTrivia)
            && !item.IsKind(SyntaxKind.EndOfLineTrivia)));
}
