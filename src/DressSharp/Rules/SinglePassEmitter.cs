using System.Text;
using DressSharp.Architecture;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DressSharp.Rules;

/// <summary>
/// Writes a formatted file in one walk, deciding whitespace as it goes rather than rewriting the
/// tree once per rule.
/// </summary>
/// <remarks>
/// Members a syntax rule wanted are handed over already rewritten, and are written from their own
/// text; everything else is written from the original source. So a file where no syntax rule applies
/// is never copied at all, and one where a rule applies pays only for the members it touched.
/// The line-break, indentation and spacing rules are carried; blank lines, comments, wrapping and
/// construct layout are not yet.
/// </remarks>
sealed class SinglePassEmitter
{
    readonly EmitterPlan _plan;
    readonly RuleContext _context;
    readonly bool _checkMalformedRegions;
    readonly StringBuilder _output;
    readonly List<Piece> _pieces;

    int _column;
    string _lineIndent = "";
    bool _previousGapHadMeaningfulTrivia;

    /// <summary>
    /// The indentation for content at each open brace, innermost last. Nesting alone decides it,
    /// so no lookup of where an owning construct started is needed.
    /// </summary>
    readonly List<string> _contentIndents = [""];
    readonly Stack<string> _braceIndents = new();

    /// <summary>
    /// One token together with the text it came from, which is the original file for most tokens and
    /// a rewritten member's own text for the rest.
    /// </summary>
    readonly record struct Piece(SyntaxToken Token, string Source);

    SinglePassEmitter(EmitterPlan plan, RuleContext context, bool checkMalformedRegions, List<Piece> pieces, int capacity)
    {
        _plan = plan;
        _context = context;
        _checkMalformedRegions = checkMalformedRegions;
        _pieces = pieces;
        _output = new StringBuilder(capacity);
    }

    internal static string Emit(
        SyntaxNode root,
        EmitterPlan plan,
        RuleContext context,
        string source,
        SyntaxRewritePlan rewrites) =>
        new SinglePassEmitter(plan, context, root.ContainsDiagnostics, Splice(root, source, rewrites), source.Length).Run();

    /// <summary>
    /// Lays the file out as a token sequence, substituting each rewritten member for the tokens it
    /// replaced.
    /// </summary>
    static List<Piece> Splice(SyntaxNode root, string source, SyntaxRewritePlan rewrites)
    {
        var pieces = new List<Piece>();
        var replacements = rewrites.Replacements;
        var next = 0;
        foreach (var token in root.DescendantTokens())
        {
            var start = token.FullSpan.Start;
            while (next < replacements.Count && start >= replacements[next].Original.End)
                next++;
            if (next >= replacements.Count || !replacements[next].Original.Contains(start))
            {
                pieces.Add(new(token, source));
                continue;
            }

            // The first token inside a replaced member brings the whole rewritten member with it;
            // the rest of the original member's tokens are then passed over.
            if (start != replacements[next].Original.Start)
                continue;
            var replacement = replacements[next];
            foreach (var rewritten in replacement.Rewritten.DescendantTokens())
                pieces.Add(new(rewritten, replacement.Text));
        }

        return pieces;
    }

    string Run()
    {
        var pair = new TokenPair();
        for (var index = 0; index < _pieces.Count; index++)
        {
            var piece = _pieces[index];
            if (index == 0)
                Copy(piece.Source, piece.Token.FullSpan.Start, piece.Token.SpanStart);
            else
                EmitGap(pair, index);

            EnterOrLeave(piece.Token);
            Append(piece.Token.Text);
        }

        if (_pieces.Count > 0)
        {
            var last = _pieces[^1];
            Copy(last.Source, last.Token.Span.End, last.Token.FullSpan.End);
        }

        return _output.ToString();
    }

    /// <summary>
    /// Writes whatever belongs between the previous token and this one.
    /// </summary>
    void EmitGap(TokenPair pair, int index)
    {
        var (left, leftSource) = _pieces[index - 1];
        var (right, rightSource) = _pieces[index];
        var boundaryBeforeLeft = _previousGapHadMeaningfulTrivia;
        _previousGapHadMeaningfulTrivia = false;

        // Inside an interpolated string the characters between tokens are content, not trivia, and
        // the braces that open a hole are not the braces the layout rules mean. Editing either
        // rewrites the string.
        if (InsideInterpolatedString(left) || InsideInterpolatedString(right))
        {
            CopyGap(index);
            return;
        }

        // Comments, directives and disabled text have to survive, and where they sit decides the
        // line structure around them, so a gap holding any of them is copied rather than decided.
        if (HasMeaningfulTrivia(left.TrailingTrivia) || HasMeaningfulTrivia(right.LeadingTrivia))
        {
            _previousGapHadMeaningfulTrivia = true;
            CopyGap(index);
            return;
        }

        // A line-break rule that owns this boundary decides it outright, in either direction.
        if (ClaimsBreak(right) is { } wantsBreak)
        {
            if (wantsBreak)
                StartLine(right);
            else
                Append(" ");
            return;
        }

        // Indentation only restates the whitespace after a line break; where the source kept two
        // tokens on one line, no indentation rule has an opinion about them.
        if (EmitUpToLastLineBreak(leftSource, left, rightSource, right))
        {
            WriteIndent(right);
            return;
        }

        // No rule owns this gap, so it keeps whatever it had. The catalog says nothing about the
        // space between a keyword and an identifier, and deciding one here would rewrite every file
        // over whitespace nobody asked about.
        pair.Reset(left, right);
        if (DesiredSpace(pair, boundaryBeforeLeft) is not { } desired)
        {
            CopyGap(index);
            return;
        }

        if (desired)
            Append(" ");
    }
    /// <summary>
    /// Writes the gap up to and including its last line break, and reports whether it had one. What
    /// follows that break is indentation the caller is about to restate.
    /// </summary>
    /// <remarks>
    /// A gap spans the previous token's trailing trivia and this one's leading trivia, which sit in
    /// different texts wherever a rewritten member begins or ends. A blank line puts a break in each,
    /// so the trailing part still has to be written when the last break is in the leading part.
    /// </remarks>
    bool EmitUpToLastLineBreak(string leftSource, SyntaxToken left, string rightSource, SyntaxToken right)
    {
        var trailingStart = left.Span.End;
        var trailingEnd = left.FullSpan.End;
        var leadingStart = right.FullSpan.Start;
        var leadingEnd = right.SpanStart;

        var inLeading = rightSource.AsSpan(leadingStart, leadingEnd - leadingStart).LastIndexOfAny(LineBreaks);
        if (inLeading >= 0)
        {
            Copy(leftSource, trailingStart, trailingEnd);
            Copy(rightSource, leadingStart, leadingStart + inLeading + 1);
            return true;
        }

        var inTrailing = leftSource.AsSpan(trailingStart, trailingEnd - trailingStart).LastIndexOfAny(LineBreaks);
        if (inTrailing < 0)
            return false;

        Copy(leftSource, trailingStart, trailingStart + inTrailing + 1);
        return true;
    }

    void CopyGap(int index)
    {
        var (left, leftSource) = _pieces[index - 1];
        var (right, rightSource) = _pieces[index];
        Copy(leftSource, left.Span.End, left.FullSpan.End);
        Copy(rightSource, right.FullSpan.Start, right.SpanStart);
    }

    bool? ClaimsBreak(SyntaxToken token)
    {
        var candidates = _plan.NewLineTrigger(token.RawKind);
        if (candidates == 0 || _checkMalformedRegions && _context.IsUnsafe(token))
            return null;
        var laterElement = (candidates & _plan.LaterElementRules) != 0
            ? NewLineRule.StartsLaterElement(token)
            : null;
        var rules = _plan.NewLines;
        for (var index = 0; index < rules.Length; index++)
        {
            if ((candidates & (1UL << index)) != 0
                && rules[index].Rule.ClaimsBreakBefore(token, rules[index].Categories, laterElement) is { } claim)
                return claim;
        }

        return null;
    }

    void StartLine(SyntaxToken token)
    {
        _output.Append(_plan.LineEnding);
        WriteIndent(token);
    }

    void WriteIndent(SyntaxToken token)
    {
        _lineIndent = token.IsKind(SyntaxKind.CloseBraceToken) && _braceIndents.Count > 0
            ? _braceIndents.Peek()
            : _contentIndents[^1];
        _output.Append(_lineIndent);
        _column = _lineIndent.Length;
    }

    /// <summary>
    /// Tracks the indentation a brace opens and closes.
    /// </summary>
    /// <remarks>
    /// A brace is indented from the content around it and its own contents from the brace, which is
    /// what <c>csharp_indent_braces</c> and <c>csharp_indent_block_contents</c> each select.
    /// </remarks>
    void EnterOrLeave(SyntaxToken token)
    {
        if (token.IsKind(SyntaxKind.OpenBraceToken))
        {
            var braceIndent = _contentIndents[^1] + (_plan.IndentBraces ? _plan.IndentUnit : "");
            _braceIndents.Push(braceIndent);
            _contentIndents.Add(braceIndent + (_plan.IndentBlockContents ? _plan.IndentUnit : ""));
        }
        else if (token.IsKind(SyntaxKind.CloseBraceToken) && _contentIndents.Count > 1)
        {
            _contentIndents.RemoveAt(_contentIndents.Count - 1);
            _braceIndents.Pop();
        }
    }

    bool? DesiredSpace(TokenPair pair, bool boundaryBeforeLeft)
    {
        var candidates = _plan.Trigger(pair.Left.RawKind) | _plan.Trigger(pair.Right.RawKind);
        if (candidates == 0)
            return null;

        var desired = default(bool?);
        var rules = _plan.Spacing;
        for (var index = 0; index < rules.Length; index++)
        {
            if ((candidates & (1UL << index)) == 0)
                continue;
            var rule = rules[index].Rule;
            var candidate = rule.DesiredSpace(pair.Left, pair.Right, rules[index].Preference);
            if (candidate is not null
                && !boundaryBeforeLeft
                && (!_checkMalformedRegions || !_context.IsUnsafe(TextSpan.FromBounds(pair.Left.FullSpan.Start, pair.Right.FullSpan.End))))
                desired = candidate;
        }

        return desired;
    }

    static bool InsideInterpolatedString(SyntaxToken token)
    {
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            if (node is Microsoft.CodeAnalysis.CSharp.Syntax.InterpolatedStringExpressionSyntax)
                return true;
            if (node is Microsoft.CodeAnalysis.CSharp.Syntax.StatementSyntax or Microsoft.CodeAnalysis.CSharp.Syntax.MemberDeclarationSyntax)
                return false;
        }

        return false;
    }

    static bool HasMeaningfulTrivia(SyntaxTriviaList trivia)
    {
        foreach (var item in trivia)
        {
            if (!item.IsKind(SyntaxKind.WhitespaceTrivia) && !item.IsKind(SyntaxKind.EndOfLineTrivia))
                return true;
        }

        return false;
    }

    void Copy(string source, int start, int end)
    {
        if (end <= start)
            return;
        _output.Append(source, start, end - start);
        var lastBreak = source.LastIndexOfAny(LineBreaks, end - 1, end - start);
        if (lastBreak < 0)
        {
            _column += end - start;
            return;
        }

        // What was preserved after the line break is the indentation this line actually starts at.
        _column = end - lastBreak - 1;
        _lineIndent = source[(lastBreak + 1)..end];
    }

    void Append(string text)
    {
        _output.Append(text);
        var lastBreak = text.LastIndexOfAny(LineBreaks);
        _column = lastBreak < 0 ? _column + text.Length : text.Length - lastBreak - 1;
    }

    static readonly char[] LineBreaks = ['\n', '\r'];
}
