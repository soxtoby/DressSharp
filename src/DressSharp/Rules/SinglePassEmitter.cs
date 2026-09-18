using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
/// The emitter carries line-break, indentation, spacing, blank-line, comment, and syntax-wrapping
/// decisions.
/// </remarks>
sealed class SinglePassEmitter
{
    readonly EmitterPlan _plan;
    readonly SyntaxWrappingPlan _syntaxWrapping;
    readonly TriviaLayoutPlan _triviaLayout;
    readonly RuleContext _context;
    readonly SyntaxNode _root;
    readonly bool _checkMalformedRegions;
    readonly StringBuilder _output;
    readonly ClaimedBreaks _claims;
    readonly EffectiveTokenStream _stream;
    readonly EffectiveTokenStream.Piece[] _pieces;
    readonly int[] _precedingStarts;

    int _column;
    string _lineIndent = "";
    string _previousLineIndent = "";
    bool _previousGapHadMeaningfulTrivia;
    SwitchSectionSyntax? _lastCaseBlockSection;
    bool _lastCaseBlockSectionIsSafe;

    /// <summary>
    /// The active brace contents, innermost last, resolved by the shared indentation model.
    /// </summary>
    readonly List<string> _contentIndents = [""];
    readonly Stack<string> _braceIndents = new();
    readonly Stack<InitializerFrame> _initializerFrames = new();
    readonly IndentationModel _indentation;
    /// <summary>
    /// Each piece's source-line indentation, by piece index. Tokens are visited in that order, so
    /// the hot reads are positional; hashing a <see cref="SyntaxToken"/> costs an identity hash on
    /// two objects and the stream has one entry per token in the file.
    /// </summary>
    readonly string[] _sourceIndents;
    Dictionary<SyntaxToken, int>? _piecesByToken;
    readonly Dictionary<ParameterListSyntax, int> _parameterListStartLines = [];
    readonly HashSet<ParameterListSyntax> _multilineParameterLists = [];
    int _lineVersion;
    bool _lastTokenStartedLine;

    /// <summary>
    /// The token being written, with the position needed to answer "does this node begin here?".
    /// </summary>
    TokenStart _current;
    int _index;
    SyntaxNode? _lastContent;
    string? _lastContentIndent;

    readonly record struct InitializerFrame(SyntaxNode Initializer, string Indent);

    SinglePassEmitter(
        EmitterPlan plan,
        EmissionLayoutPlan layout,
        RuleContext context,
        SyntaxNode root,
        bool checkMalformedRegions,
        int capacity,
        bool writes)
    {
        _plan = plan;
        _claims = layout.Claims;
        _stream = layout.Stream;
        _indentation = layout.Indentation;
        _syntaxWrapping = layout.Wrapping;
        _triviaLayout = layout.Trivia;
        _context = context;
        _root = root;
        _checkMalformedRegions = checkMalformedRegions;
        _pieces = layout.Stream.Pieces;
        _precedingStarts = layout.Stream.PrecedingContentStarts();
        _output = new StringBuilder(capacity);
        if (!writes)
        {
            // Reading the line structure never restates a column, so the indentation every token
            // started its source line with is a cost only a run that writes the file has to pay.
            _sourceIndents = [];
            return;
        }

        var segmentStarts = new Dictionary<int, int>();
        foreach (var piece in _pieces)
        {
            if (!piece.IsOriginal)
                segmentStarts.TryAdd(piece.SegmentIndex, piece.Token.FullSpan.Start);
        }

        _sourceIndents = new string[_pieces.Length];
        for (var index = 0; index < _pieces.Length; index++)
        {
            var piece = _pieces[index];
            var position = piece.IsOriginal
                ? piece.Token.SpanStart
                : piece.Token.SpanStart - segmentStarts[piece.SegmentIndex];
            _sourceIndents[index] = SourceIndent(piece.Source, position);
        }
    }

    internal static string Emit(
        SyntaxNode root,
        EmitterPlan plan,
        RuleContext context,
        string source,
        EmissionLayoutPlan layout) =>
        new SinglePassEmitter(
            plan,
            layout,
            context,
            root,
            root.ContainsDiagnostics,
            source.Length,
            writes: true).Run();

    /// <summary>
    /// An emitter that answers where the lines will fall without writing the file.
    /// </summary>
    internal static SinglePassEmitter ForReading(
        SyntaxNode root,
        EmitterPlan plan,
        RuleContext context,
        EmissionLayoutPlan layout) =>
        new(plan, layout, context, root, root.ContainsDiagnostics, capacity: 0, writes: false);

    string Run()
    {
        var pair = new TokenPair();
        for (var index = 0; index < _pieces.Length; index++)
        {
            var piece = _pieces[index];
            _index = index;
            _current = new(piece.Token, _precedingStarts[index]);
            if (index == 0)
            {
                if (_triviaLayout.HasLeadingEdit(index))
                    AppendTrivia(_triviaLayout.Leading(index));
                else
                    Copy(piece.Source, piece.Token.FullSpan.Start, piece.Token.SpanStart);
            }
            else
            {
                EnterInitializer(piece.Token, index);
                EmitGap(pair, index);
            }

            RememberContentIndents(piece.Token);
            EnterOrLeave(piece.Token);
            _lastTokenStartedLine = _column == _lineIndent.Length;
            Append(_indentation.RebaseTokenText(piece.Token, _sourceIndents[index], _lineIndent));
            RememberParameterListLine(piece.Token);
        }

        if (_pieces.Length > 0)
        {
            var last = _pieces[^1];
            if (_triviaLayout.HasTrailingEdit(_pieces.Length - 1))
                AppendTrivia(_triviaLayout.Trailing(_pieces.Length - 1));
            else
                Copy(last.Source, last.Token.Span.End, last.Token.FullSpan.End);
        }

        return _output.ToString();
    }

    void EnterInitializer(SyntaxToken token, int index)
    {
        if (InitializerIndentationRule.FindDelimiter(token) is not { IsOpening: true } delimiter
            || _plan.InitializerIndentation(delimiter.Kind) is not { } indented
            || delimiter.Initializer.ContainsDirectives
            || _checkMalformedRegions && IsUnsafeOriginal(delimiter.Initializer))
        {
            return;
        }

        var baseIndent = delimiter.Kind == InitializerKind.CollectionExpressionArgument
            ? ArgumentIndent(token, index)
            : _lineIndent;
        _initializerFrames.Push(new(
            delimiter.Initializer,
            baseIndent + (indented ? _plan.IndentUnit : "")));
    }

    string ArgumentIndent(SyntaxToken token, int index)
    {
        if (_syntaxWrapping.GapBefore(index) is { } gap
            && gap.LastIndexOfAny(LineBreaks) is var lastBreak and >= 0)
        {
            return gap[(lastBreak + 1)..];
        }

        if (_plan.IndentBlockContents is not null
            && token.Parent is CollectionExpressionSyntax collection)
        {
            SyntaxNode expression = collection;
            while (expression.Parent is ParenthesizedExpressionSyntax or CastExpressionSyntax)
                expression = expression.Parent;
            if (expression.Parent is ArgumentSyntax { Parent: { } argumentList })
                return _indentation.Continuation(argumentList, 1, "");
        }

        return _lineIndent;
    }

    static bool OpensOrClosesGeneratedBlock(
        EffectiveTokenStream.Piece leftPiece,
        EffectiveTokenStream.Piece rightPiece) =>
        !leftPiece.IsOriginal
            && leftPiece.Token.IsKind(SyntaxKind.OpenBraceToken)
            && leftPiece.Token.Parent is BlockSyntax leftBlock
            && leftBlock.HasAnnotation(GeneratedSyntax.Block)
        || !rightPiece.IsOriginal
            && rightPiece.Token.IsKind(SyntaxKind.CloseBraceToken)
            && rightPiece.Token.Parent is BlockSyntax rightBlock
            && rightBlock.HasAnnotation(GeneratedSyntax.Block);

    /// <summary>
    /// Whether the planned layout puts a line break anywhere inside <paramref name="node"/>.
    /// </summary>
    /// <remarks>
    /// Answered for a node of the tree the plan was built from, or of the member the rewrite plan
    /// put in its place, named by <paramref name="segment"/>. A node the stream never saw — one a
    /// rule has just built — falls back to the lines its own text is written over.
    /// </remarks>
    internal bool SpansLines(SyntaxNode node, int segment)
    {
        if (SpansLines(node.GetFirstToken(), node.GetLastToken(), segment) is { } planned)
            return planned;

        var span = node.GetLocation().GetLineSpan();
        return span.StartLinePosition.Line != span.EndLinePosition.Line;
    }

    /// <summary>
    /// Whether the planned layout puts a line break between these two tokens, or null when either
    /// is absent from the stream.
    /// </summary>
    internal bool? SpansLines(SyntaxToken first, SyntaxToken last, int segment)
    {
        if (!_stream.TryIndexOf(first, segment, out var start)
            || !_stream.TryIndexOf(last, segment, out var end))
        {
            return null;
        }

        for (var index = start; index <= end; index++)
        {
            _index = index;
            _current = new(_pieces[index].Token, _precedingStarts[index]);
            if (index > start && BreaksBefore(index))
                return true;
            if (_pieces[index].Token.Text.AsSpan().IndexOfAny(LineBreaks) >= 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the gap before this token will hold a line break, in the order
    /// <see cref="EmitGap"/> settles gaps in.
    /// </summary>
    /// <remarks>
    /// Only the reading emitter asks, and only until the first break it finds, so the branches
    /// <see cref="EmitGap"/> reaches through a parameter list that has already been seen to span
    /// lines cannot be reached here: they need a break this walk would have stopped at.
    /// </remarks>
    bool BreaksBefore(int index)
    {
        var leftPiece = _pieces[index - 1];
        var rightPiece = _pieces[index];
        var left = leftPiece.Token;
        var right = rightPiece.Token;

        if (InsideInterpolatedString(left, afterToken: true)
            || InsideInterpolatedString(right, afterToken: false))
        {
            return GapBreaks(index);
        }

        if (_triviaLayout.HasMeaningfulGap(index))
        {
            return _syntaxWrapping.GapBefore(index) is { } commentGap
                ? commentGap.AsSpan().IndexOfAny(LineBreaks) >= 0
                : GapBreaks(index);
        }

        if (_syntaxWrapping.GapBefore(index - 1) is not null
            && _syntaxWrapping.GapBefore(index) is null
            && IsWrappedExpressionOperator(left))
        {
            return false;
        }

        if (OpensOrClosesGeneratedBlock(leftPiece, rightPiece))
            return true;

        if (ClaimsBreak(left, right) is { } claimed)
            return claimed;

        if (_syntaxWrapping.GapBefore(index) is { } gap)
            return gap.AsSpan().IndexOfAny(LineBreaks) >= 0;

        if ((_plan.ExpandSingleLineBlocks || _plan.SeparateSingleLineStatements)
            && PreservationBreaks(left, right) != 0)
        {
            return true;
        }

        // Whatever is left keeps the gap it had, whether that is copied whole or restated down to
        // its last break.
        return GapBreaks(index);
    }

    bool GapBreaks(int index) =>
        Breaks(_triviaLayout.Trailing(index - 1)) || Breaks(_triviaLayout.Leading(index));

    static bool Breaks(SyntaxTriviaList trivia)
    {
        foreach (var item in trivia)
        {
            if (item.IsKind(SyntaxKind.EndOfLineTrivia))
                return true;
            if (!item.IsKind(SyntaxKind.WhitespaceTrivia)
                && item.ToFullString().AsSpan().IndexOfAny(LineBreaks) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Writes whatever belongs between the previous token and this one.
    /// </summary>
    void EmitGap(TokenPair pair, int index)
    {
        var leftPiece = _pieces[index - 1];
        var rightPiece = _pieces[index];
        var left = leftPiece.Token;
        var right = rightPiece.Token;
        var boundaryBeforeLeft = _previousGapHadMeaningfulTrivia;
        _previousGapHadMeaningfulTrivia = false;

        // Inside an interpolated string the characters between tokens are content, not trivia, and
        // the braces that open a hole are not the braces the layout rules mean. Editing either
        // rewrites the string.
        if (InsideInterpolatedString(left, afterToken: true) || InsideInterpolatedString(right, afterToken: false))
        {
            CopyGap(index);
            return;
        }

        // Comments, directives and disabled text have to survive, and where they sit decides the
        // line structure around them, so a gap holding any of them is copied rather than decided.
        if (_triviaLayout.HasMeaningfulGap(index))
        {
            _previousGapHadMeaningfulTrivia = true;
            if (_syntaxWrapping.GapBefore(index) is { } wrappingGap)
                EmitWrappingGap(wrappingGap, right);
            else
                CopyCommentGap(index, right);
            return;
        }

        if (_syntaxWrapping.GapBefore(index - 1) is not null
            && _syntaxWrapping.GapBefore(index) is null
            && IsWrappedExpressionOperator(left))
        {
            pair.Reset(left, right);
            if (DesiredSpace(
                    pair,
                    boundaryBeforeLeft,
                    leftPiece.IsOriginal && rightPiece.IsOriginal) != false)
            {
                Append(" ");
            }
            return;
        }

        if (OpensOrClosesGeneratedBlock(leftPiece, rightPiece))
        {
            StartLine(right);
            return;
        }

        if (MultilineParameterCloseBreak(right) is { } parameterCloseBreak)
        {
            if (parameterCloseBreak)
            {
                if (_syntaxWrapping.GapBefore(index) is { } wrappingGap)
                    EmitWrappingGap(wrappingGap, right);
                else
                    StartParameterCloseLine((ParameterListSyntax)right.Parent!, right);
            }
            return;
        }

        if (AttachesConstructorInitializer(left, right))
        {
            Append(" ");
            return;
        }

        // Embedded statement placement and the new-line rules follow syntax wrapping in catalog
        // order, so they get the last word on a boundary both rules own.
        if (ClaimsBreak(left, right) is { } wantsBreak)
        {
            if (wantsBreak)
                StartLine(right);
            else
                Append(" ");
            return;
        }

        if (_syntaxWrapping.GapBefore(index) is { } gap)
        {
            EmitWrappingGap(gap, right);
            return;
        }

        var preservationBreaks = _plan.ExpandSingleLineBlocks || _plan.SeparateSingleLineStatements
            ? PreservationBreaks(left, right)
            : 0;
        if (preservationBreaks != 0)
        {
            for (var count = 0; count < preservationBreaks; count++)
                Append(_context.LineEnding);
            WriteIndent(right);
            return;
        }

        if (PreservesSourceIndent(right)
            && _triviaLayout.HasLineBreak(index))
        {
            CopyGap(index);
            return;
        }

        // Indentation only restates the whitespace after a line break; where the source kept two
        // tokens on one line, no indentation rule has an opinion about them.
        if (EmitUpToLastLineBreak(
                index,
                leftPiece.Source,
                left,
                rightPiece.Source,
                right))
        {
            WriteIndent(right);
            return;
        }

        // No rule owns this gap, so it keeps whatever it had. The catalog says nothing about the
        // space between a keyword and an identifier, and deciding one here would rewrite every file
        // over whitespace nobody asked about.
        pair.Reset(left, right);
        if (DesiredSpace(
                pair,
                boundaryBeforeLeft,
                leftPiece.IsOriginal && rightPiece.IsOriginal) is not { } desired)
        {
            CopyGap(index);
            return;
        }

        if (desired)
            Append(" ");
    }

    void EmitWrappingGap(string gap, SyntaxToken right)
    {
        var lastBreak = gap.LastIndexOfAny(LineBreaks);
        if (lastBreak >= 0 && InitializerIndentFor(right) is not null)
        {
            var indentStart = gap.Length;
            while (indentStart > lastBreak + 1 && gap[indentStart - 1] is ' ' or '\t')
                indentStart--;
            Append(gap[..indentStart]);
            WriteIndent(right);
            return;
        }

        Append(gap);
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
    bool EmitUpToLastLineBreak(
        int rightIndex,
        string leftSource,
        SyntaxToken left,
        string rightSource,
        SyntaxToken right)
    {
        if (_triviaLayout.HasGapEdit(rightIndex))
        {
            var leading = _triviaLayout.Leading(rightIndex).ToFullString();
            var inLeadingEdit = leading.LastIndexOfAny(LineBreaks);
            if (inLeadingEdit >= 0)
            {
                AppendTrivia(_triviaLayout.Trailing(rightIndex - 1));
                Append(leading[..(inLeadingEdit + 1)]);
                return true;
            }

            var trailing = _triviaLayout.Trailing(rightIndex - 1).ToFullString();
            var inTrailingEdit = trailing.LastIndexOfAny(LineBreaks);
            if (inTrailingEdit < 0)
                return false;
            Append(trailing[..(inTrailingEdit + 1)]);
            return true;
        }

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

    /// <summary>
    /// Copies a gap holding comments, giving the indentation inside it to the token that follows.
    /// </summary>
    /// <remarks>
    /// A comment has to survive exactly, but the whitespace around it is layout rather than comment,
    /// and copying the gap whole hands that whitespace to the source. The token after the gap then
    /// keeps its original column however far its owner moved, and its comment holds it there. A
    /// comment describes the code below it, so alignment puts it where that code goes; without
    /// alignment, moving every line in the gap by the same distance leaves the comment where its
    /// author put it relative to that code while letting both follow the code's new position.
    /// With no indentation preference the code goes where its author put it, and alignment is
    /// the only thing that moves: the comment takes the indentation the code already has.
    /// </remarks>
    void CopyCommentGap(int index, SyntaxToken right)
    {
        if (!HoldsOnlyComments(index) || !OwnsItsLine(right))
        {
            CopyGap(index);
            return;
        }

        var planned = _plan.IndentBlockContents is not null ? IndentFor(right) : null;
        var start = _output.Length;
        var column = _column;
        var lineIndent = _lineIndent;
        var previousLineIndent = _previousLineIndent;
        var lineVersion = _lineVersion;
        CopyGap(index);
        if ((planned ?? KeptIndent(start)) is not { } indent
            || (_plan.AlignComments ? AlignedGap(start, indent) : RebasedGap(start, indent)) is not { } rebased)
        {
            return;
        }

        _output.Length = start;
        _column = column;
        _lineIndent = lineIndent;
        _previousLineIndent = previousLineIndent;
        _lineVersion = lineVersion;
        Append(rebased);
    }

    /// <summary>
    /// Whether the indent <see cref="IndentFor"/> gives <paramref name="token"/> is the one it would
    /// start a line with.
    /// </summary>
    /// <remarks>
    /// Only a token that opens a piece of content its owner lays out answers here. A continuation
    /// inside an expression starts its line too, but wrapping decides how far in it goes, and asking
    /// this model instead would flatten it against its statement. Without an indentation
    /// preference the line is left where it is, and only a comment being aligned to it cares.
    /// </remarks>
    bool OwnsItsLine(SyntaxToken token) =>
        (_plan.IndentBlockContents is not null || _plan.AlignComments)
        && ((_indentation.DirectContentFor(token) is { } content
                && content.GetFirstToken(includeZeroWidth: true) == token)
            || (token.Parent is SwitchLabelSyntax label
                && label.GetFirstToken(includeZeroWidth: true) == token));

    bool HoldsOnlyComments(int index)
    {
        var comment = false;
        foreach (var trivia in _triviaLayout.Trailing(index - 1))
        {
            if (!IsCommentOrSpace(trivia, ref comment))
                return false;
        }
        foreach (var trivia in _triviaLayout.Leading(index))
        {
            if (!IsCommentOrSpace(trivia, ref comment))
                return false;
        }

        return comment;
    }

    static bool IsCommentOrSpace(SyntaxTrivia trivia, ref bool comment)
    {
        switch (trivia.Kind())
        {
            case SyntaxKind.SingleLineCommentTrivia:
            case SyntaxKind.MultiLineCommentTrivia:
            case SyntaxKind.SingleLineDocumentationCommentTrivia:
            case SyntaxKind.MultiLineDocumentationCommentTrivia:
                comment = true;
                return true;
            case SyntaxKind.WhitespaceTrivia:
            case SyntaxKind.EndOfLineTrivia:
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// The indentation the copied gap leaves its following token with, or null when that token
    /// does not start a line of its own.
    /// </summary>
    string? KeptIndent(int start)
    {
        var text = _output.ToString(start, _output.Length - start);
        var lastBreak = text.AsSpan().LastIndexOfAny(LineBreaks);
        if (lastBreak < 0)
            return null;

        var kept = text[(lastBreak + 1)..];
        return kept.Length == InitialIndentLength(kept) ? kept : null;
    }

    /// <summary>
    /// The copied gap with every line it starts moved from <c>source</c> to <c>indent</c>, or null
    /// when the gap does not start a line or already sits where it belongs.
    /// </summary>
    string? RebasedGap(int start, string indent)
    {
        var text = _output.ToString(start, _output.Length - start);
        var lastBreak = text.AsSpan().LastIndexOfAny('\n', '\r');
        if (lastBreak < 0)
            return null;

        var source = text[(lastBreak + 1)..];
        if (source.Length != InitialIndentLength(source) || source == indent)
            return null;

        var output = new StringBuilder(text.Length);
        var position = 0;
        while (position < text.Length)
        {
            var next = text.AsSpan(position).IndexOfAny('\n', '\r');
            if (next < 0)
            {
                output.Append(text, position, text.Length - position);
                break;
            }

            var breakStart = position + next;
            var breakEnd = breakStart + (text.AsSpan(breakStart).StartsWith("\r\n") ? 2 : 1);
            output.Append(text, position, breakEnd - position);
            var lineIndent = text.AsSpan(breakEnd);
            var length = InitialIndentLength(lineIndent);
            output.Append(IndentationModel.Rebase(source, indent, lineIndent[..length].ToString())
                ?? Outdented(source, indent, lineIndent[..length])
                ?? lineIndent[..length].ToString());
            position = breakEnd + length;
        }

        return output.ToString();
    }

    /// <summary>
    /// Where a line that starts short of the code it leads goes when that code moves from
    /// <paramref name="source"/> to <paramref name="indent"/>, or null when the line is not simply
    /// short of the code.
    /// </summary>
    /// <remarks>
    /// The line keeps its distance from the code, as a line that starts beyond the code keeps its.
    /// It cannot go further out than the margin, so a line the code overtakes lands there.
    /// </remarks>
    static string? Outdented(string source, string indent, ReadOnlySpan<char> lineIndent)
    {
        if (!source.AsSpan().StartsWith(lineIndent))
            return null;

        var distance = source.Length - lineIndent.Length;
        return indent[..Math.Max(0, indent.Length - distance)];
    }

    /// <summary>
    /// The copied gap with every comment in it moved to <paramref name="indent"/>, or null when the
    /// gap starts no line a comment leads.
    /// </summary>
    /// <remarks>
    /// A comment stands in for the code it leads, so it takes the column that code takes. Only a
    /// line a comment starts answers to that: the rest of a block comment is the comment's own text,
    /// which keeps its shape by moving as far as the line that opened it moved. A block whose lines
    /// do not all start where that line starts has no shape to keep, and stays where it is.
    /// </remarks>
    string? AlignedGap(int start, string indent)
    {
        var text = _output.ToString(start, _output.Length - start);
        if (BlockComments(text) is not { } blocks)
            return null;

        var output = new StringBuilder(text.Length);
        var position = 0;
        var changed = false;
        while (position < text.Length)
        {
            var next = text.AsSpan(position).IndexOfAny('\n', '\r');
            if (next < 0)
            {
                output.Append(text, position, text.Length - position);
                break;
            }

            var breakStart = position + next;
            var breakEnd = breakStart + (text.AsSpan(breakStart).StartsWith("\r\n") ? 2 : 1);
            output.Append(text, position, breakEnd - position);
            var length = InitialIndentLength(text.AsSpan(breakEnd));
            var lineIndent = text.Substring(breakEnd, length);
            // A line with nothing on it holds no comment and would only gain trailing whitespace.
            var aligned = EmptyLine(text, breakEnd + length)
                ? lineIndent
                : Aligned(blocks, breakEnd, LineEnd(text, breakEnd + length), lineIndent, indent);
            changed |= aligned != lineIndent;
            output.Append(aligned);
            position = breakEnd + length;
        }

        return changed ? output.ToString() : null;
    }

    /// <summary>
    /// Where a line reading <paramref name="lineIndent"/> starts once the comment on it is aligned.
    /// </summary>
    static string Aligned(List<BlockComment> blocks, int lineStart, int lineEnd, string lineIndent, string indent)
    {
        foreach (var block in blocks)
        {
            if (block.Close < lineStart)
                continue;
            if (block.Open >= lineEnd)
                break;
            if (!block.Aligns)
                return lineIndent;

            return block.Open < lineStart
                ? IndentationModel.Rebase(block.Indent!, indent, lineIndent) ?? lineIndent
                : indent;
        }

        return indent;
    }

    /// <summary>
    /// Where each block comment in <paramref name="text"/> runs, or null when a comment is unclosed.
    /// </summary>
    static List<BlockComment>? BlockComments(string text)
    {
        List<BlockComment> blocks = [];
        for (var position = 0; position + 1 < text.Length; position++)
        {
            if (text[position] != '/')
                continue;
            if (text[position + 1] == '/')
            {
                var lineEnd = text.AsSpan(position).IndexOfAny('\n', '\r');
                if (lineEnd < 0)
                    break;
                position += lineEnd;
                continue;
            }
            if (text[position + 1] != '*')
                continue;

            var close = text.IndexOf("*/", position + 2, StringComparison.Ordinal);
            if (close < 0)
                return null;

            // The gap opens partway through the line its first token sits on, so a comment starting
            // there has no indentation of its own to read, and nothing says where its lines belong.
            var lineStart = text.AsSpan(0, position).LastIndexOfAny('\n', '\r') + 1;
            var openIndent = lineStart == 0
                ? null
                : text.Substring(lineStart, InitialIndentLength(text.AsSpan(lineStart)));
            blocks.Add(new(position, close + 1, openIndent, openIndent is not null && Shaped(text, position, close, openIndent)));
            position = close + 1;
        }

        return blocks;
    }

    /// <summary>
    /// Whether every line of a block comment starts where the line that opened it starts.
    /// </summary>
    static bool Shaped(string text, int open, int close, string openIndent)
    {
        for (var position = open; position < close;)
        {
            var next = text.AsSpan(position, close - position).IndexOfAny('\n', '\r');
            if (next < 0)
                return true;

            var breakStart = position + next;
            var breakEnd = breakStart + (text.AsSpan(breakStart).StartsWith("\r\n") ? 2 : 1);
            var length = InitialIndentLength(text.AsSpan(breakEnd));
            if (!EmptyLine(text, breakEnd + length)
                && !text.AsSpan(breakEnd, length).StartsWith(openIndent, StringComparison.Ordinal))
            {
                return false;
            }

            position = breakEnd + length;
        }

        return true;
    }

    /// <summary>
    /// Whether nothing follows the indentation at <paramref name="position"/> on its line. The
    /// gap runs out at the token it precedes, so its last line is that token's, not an empty one.
    /// </summary>
    static bool EmptyLine(string text, int position) =>
        position < text.Length && text[position] is '\n' or '\r';

    static int LineEnd(string text, int position)
    {
        var next = text.AsSpan(position).IndexOfAny('\n', '\r');
        return next < 0 ? text.Length : position + next;
    }

    /// <summary>
    /// A block comment, the indentation of the line it opened on, and whether its lines follow that
    /// line closely enough to move with it.
    /// </summary>
    readonly record struct BlockComment(int Open, int Close, string? Indent, bool Aligns);

    void CopyGap(int index)
    {
        if (_triviaLayout.HasGapEdit(index))
        {
            AppendTrivia(_triviaLayout.Trailing(index - 1));
            AppendTrivia(_triviaLayout.Leading(index));
            return;
        }

        var left = _pieces[index - 1];
        var right = _pieces[index];
        Copy(left.Source, left.Token.Span.End, left.Token.FullSpan.End);
        Copy(right.Source, right.Token.FullSpan.Start, right.Token.SpanStart);
    }

    bool? MultilineParameterCloseBreak(SyntaxToken token)
    {
        if (_plan.MultilineParametersClosingParenthesisPosition is not { } position
            || !token.IsKind(SyntaxKind.CloseParenToken)
            || token.Parent is not ParameterListSyntax { Parameters.Count: > 0 } parameters
            || !_parameterListStartLines.TryGetValue(parameters, out var startLine)
            || startLine == _lineVersion
            || _checkMalformedRegions && IsUnsafeOriginal(token))
        {
            return null;
        }

        return position == "own_line";
    }

    bool AttachesConstructorInitializer(SyntaxToken left, SyntaxToken right) =>
        _plan.MultilineParametersClosingParenthesisPosition is not null
        && right.IsKind(SyntaxKind.ColonToken)
        && right.Parent is ConstructorInitializerSyntax { Parent: ConstructorDeclarationSyntax constructor }
        && left == constructor.ParameterList.CloseParenToken
        && _multilineParameterLists.Contains(constructor.ParameterList)
        && (!_checkMalformedRegions || !IsUnsafeOriginal(right));

    void StartParameterCloseLine(ParameterListSyntax parameters, SyntaxToken close)
    {
        Append(_context.LineEnding);
        if (_plan.IndentBlockContents is not null)
        {
            _lineIndent = _indentation.ForNode(parameters.Parent);
            Append(_lineIndent);
        }
        else
        {
            _lineIndent = SourceIndentAt(parameters.OpenParenToken);
            Append(_lineIndent);
        }
    }

    void RememberParameterListLine(SyntaxToken token)
    {
        if (token.Parent is not ParameterListSyntax parameters)
            return;
        if (token == parameters.OpenParenToken)
            _parameterListStartLines[parameters] = _lineVersion;
        else if (token == parameters.CloseParenToken)
        {
            if (_parameterListStartLines.GetValueOrDefault(parameters, _lineVersion) != _lineVersion)
                _multilineParameterLists.Add(parameters);
            _parameterListStartLines.Remove(parameters);
        }
    }

    void StartLine(SyntaxToken token)
    {
        Append(_context.LineEnding);
        WriteIndent(token);
    }

    void WriteIndent(SyntaxToken token)
    {
        _lineIndent = IndentFor(token);
        _output.Append(_lineIndent);
        _column = _lineIndent.Length;
    }

    string IndentFor(SyntaxToken token)
    {
        if (token.IsKind(SyntaxKind.OpenBraceToken)
            && PropertyPatternBraceIndent(token) is { } propertyPatternIndent)
            return propertyPatternIndent;

        if (SwitchExpressionIndentFor(token) is { } switchExpressionIndent)
            return switchExpressionIndent;

        if (LambdaBlockIndentFor(token) is { } lambdaIndent)
            return lambdaIndent;

        if (InitializerIndentFor(token) is { } initializerIndent)
            return initializerIndent;

        if (_plan.LabelIndentation is { } labelIndentation
            && LabelFor(token) is { } labeledStatement
            && (!_checkMalformedRegions || !IsUnsafeOriginal(labeledStatement)))
        {
            return labelIndentation switch
            {
                LabelIndentationStyle.FlushLeft => "",
                LabelIndentationStyle.OneLessThanCurrent => RemoveIndentUnit(_contentIndents[^1]),
                _ => _contentIndents[^1]
            };
        }

        if (_plan.IndentSwitchLabels is not null
            && token.Parent is SwitchLabelSyntax label
            && BeginsAt(label, token)
            && (!_checkMalformedRegions || !IsUnsafeOriginal(label)))
        {
            return _indentation.SwitchLabel((SwitchSectionSyntax)label.Parent!);
        }

        if (CaseBlockIndent(token) is { } caseBlockIndent)
            return caseBlockIndent;

        if (token.IsKind(SyntaxKind.OpenBraceToken)
            && token.Parent is SwitchExpressionSyntax
            && _plan.IndentBraces is { } indentSwitchExpressionBrace)
        {
            return _previousLineIndent + (indentSwitchExpressionBrace ? _plan.IndentUnit : "");
        }

        if (token.IsKind(SyntaxKind.OpenBraceToken)
            && _plan.IndentBraces is { } indentBrace)
        {
            return BraceBaseIndent(token) + (indentBrace ? _plan.IndentUnit : "");
        }

        if (EmbeddedStatements.StartsBody(FirstStatementAt(token), out var embeddedStatement)
            && (!_checkMalformedRegions || !IsUnsafeOriginal(embeddedStatement)))
        {
            return _indentation.ForNode(embeddedStatement);
        }

        if (_plan.IndentCaseContents is not null
            && DirectSwitchSectionStatement(token) is { } section
            && (!_checkMalformedRegions || !IsUnsafeOriginal(section)))
        {
            return _indentation.ForNode(token.Parent?.FirstAncestorOrSelf<StatementSyntax>());
        }

        if (_plan.IndentBlockContents is not null
            && !token.IsKind(SyntaxKind.OpenBraceToken)
            && !token.IsKind(SyntaxKind.CloseBraceToken)
            && ContinuationIndentFor(token) is { } continuationIndent)
        {
            return continuationIndent;
        }

        return token.IsKind(SyntaxKind.CloseBraceToken) && _braceIndents.Count != 0
            ? _braceIndents.Peek()
            : token.Parent is ElseClauseSyntax or CatchClauseSyntax or FinallyClauseSyntax
                ? _indentation.ForNode(token.Parent)
                : _contentIndents[^1];
    }

    string? SwitchExpressionIndentFor(SyntaxToken token)
    {
        if (_plan.IndentSwitchExpression is not { } indented
            || SwitchExpressionFor(token) is not { } expression
            || expression.ContainsDirectives
            || _checkMalformedRegions && IsUnsafeOriginal(expression))
        {
            return null;
        }

        return token == expression.OpenBraceToken
            ? _previousLineIndent + (indented ? _plan.IndentUnit : "")
            : _braceIndents.TryPeek(out var braceIndent) ? braceIndent : null;
    }

    static SwitchExpressionSyntax? SwitchExpressionFor(SyntaxToken token) =>
        token.Parent is SwitchExpressionSyntax expression
        && (token == expression.OpenBraceToken || token == expression.CloseBraceToken)
            ? expression
            : null;

    string? LambdaBlockIndentFor(SyntaxToken token)
    {
        if (_plan.IndentLambdaBlock is not { } indented
            || token.Parent is not BlockSyntax { Parent: LambdaExpressionSyntax lambda } block
            || token != block.OpenBraceToken && token != block.CloseBraceToken
            || block.ContainsDirectives
            || _checkMalformedRegions && IsUnsafeOriginal(lambda)
            || !_indentation.TryGet(lambda, out var lambdaIndent))
        {
            return null;
        }

        return lambdaIndent + (indented ? _plan.IndentUnit : "");
    }

    string BraceBaseIndent(SyntaxToken token)
    {
        if (PropertyPatternBraceIndent(token) is { } propertyPatternIndent)
            return propertyPatternIndent;

        if (token.Parent is BlockSyntax { Parent: AnonymousFunctionExpressionSyntax function }
            && _indentation.TryGet(function, out var functionIndent))
            return functionIndent;

        if (_plan.IndentBlockContents is not null && token.Parent is { } braceOwner)
            return _indentation.ForNode(braceOwner);

        if (_plan.IndentBlockContents is null)
        {
            var owner = token.Parent is BlockSyntax or AccessorListSyntax ? token.Parent.Parent : token.Parent;
            if (owner is not null && _indentation.TryGet(owner, out var ownerIndent))
                return ownerIndent;
            if (owner is not null && SourceIndentOf(owner.GetFirstToken()) is { } sourceIndent)
                return sourceIndent;
        }

        return _contentIndents[^1];
    }

    string? PropertyPatternBraceIndent(SyntaxToken token)
    {
        if (token.Parent is not PropertyPatternClauseSyntax { Parent: RecursivePatternSyntax pattern }
            || pattern.Parent is not IsPatternExpressionSyntax isPattern)
        {
            return null;
        }

        var anchor = pattern.Type?.GetFirstToken() ?? isPattern.GetFirstToken();
        return _indentation.ExistingContinuation(anchor) is { } indent
            ? indent + (pattern.Type is null ? _plan.IndentUnit : "")
            : null;
    }

    SwitchSectionSyntax? DirectSwitchSectionStatement(SyntaxToken token)
    {
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            if (node is not StatementSyntax statement)
                continue;
            return statement.Parent is SwitchSectionSyntax section && BeginsAt(statement, token)
                ? section
                : null;
        }

        return null;
    }

    string? CaseBlockIndent(SyntaxToken token)
    {
        if (_plan.IndentCaseContentsWhenBlock is null
            || DirectSwitchSectionBlock(token) is not { } section
            || !CaseBlockSectionIsSafe(section))
        {
            return null;
        }

        return _indentation.Brace(token.Parent!);
    }

    bool CaseBlockSectionIsSafe(SwitchSectionSyntax section)
    {
        if (ReferenceEquals(_lastCaseBlockSection, section))
            return _lastCaseBlockSectionIsSafe;

        _lastCaseBlockSection = section;
        _lastCaseBlockSectionIsSafe = !_checkMalformedRegions || !IsUnsafeOriginal(section);
        return _lastCaseBlockSectionIsSafe;
    }

    int PreservationBreaks(SyntaxToken left, SyntaxToken right)
    {
        if (_plan.ExpandSingleLineBlocks
            && SingleLineBraceOwner(left, opening: true) is { } leftContainer
            && IsSafeSingleLine(leftContainer))
        {
            return 1;
        }

        if (_plan.ExpandSingleLineBlocks
            && SingleLineBraceOwner(right, opening: false) is { } rightContainer
            && IsSafeSingleLine(rightContainer))
        {
            return 1;
        }

        if (_plan.SeparateSingleLineStatements
            && FirstStatementAt(right) is { Parent: { } statementParent and (BlockSyntax or SwitchSectionSyntax) } statement
            && PreviousStatementIn(statementParent, statement) is { } previous
            && SharesLine(previous, statement)
            && IsSafePreservationContainer(statementParent))
        {
            return 1;
        }

        if (_plan.SeparateSingleLineStatements
            && FirstMemberAt(right) is { Parent: { } parent } member
            && PreviousMemberIn(parent, member) is { } previousMember
            && SharesLine(previousMember, member)
            && CanRewritePreservingTrivia(previousMember)
            && CanRewritePreservingTrivia(member))
        {
            return 1;
        }

        return 0;
    }

    static StatementSyntax? PreviousStatementIn(SyntaxNode parent, StatementSyntax statement)
    {
        for (var node = statement.GetFirstToken().GetPreviousToken().Parent; node is not null; node = node.Parent)
        {
            if (node is StatementSyntax previous && previous.Parent == parent)
                return previous;
            if (node == parent)
                return null;
        }

        return null;
    }

    static MemberDeclarationSyntax? PreviousMemberIn(SyntaxNode parent, MemberDeclarationSyntax member)
    {
        for (var node = member.GetFirstToken().GetPreviousToken().Parent; node is not null; node = node.Parent)
        {
            if (node is MemberDeclarationSyntax previous && previous.Parent == parent)
                return previous;
            if (node == parent)
                return null;
        }

        return null;
    }

    static bool SharesLine(SyntaxNode left, SyntaxNode right) =>
        left.GetLocation().GetLineSpan().EndLinePosition.Line
        == right.GetLocation().GetLineSpan().StartLinePosition.Line;

    StatementSyntax? FirstStatementAt(SyntaxToken token)
    {
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            if (node is StatementSyntax statement)
                return BeginsAt(statement, token) ? statement : null;
            if (node is MemberDeclarationSyntax)
                return null;
        }

        return null;
    }

    MemberDeclarationSyntax? FirstMemberAt(SyntaxToken token)
    {
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            if (node is MemberDeclarationSyntax member)
                return BeginsAt(member, token) ? member : null;
        }

        return null;
    }

    static SwitchSectionSyntax? DirectSwitchSectionBlock(SyntaxToken token) =>
        token.Parent is BlockSyntax { Parent: SwitchSectionSyntax section } block
        && (token == block.OpenBraceToken || token == block.CloseBraceToken)
            ? section
            : null;

    bool PreservesSourceIndent(SyntaxToken token)
    {
        if (_plan.IndentSwitchExpression is not null
            && SwitchExpressionFor(token) is { } switchExpression)
        {
            return switchExpression.ContainsDirectives
                || _checkMalformedRegions && IsUnsafeOriginal(switchExpression);
        }

        if (LambdaBlockIndentFor(token) is not null)
            return false;

        if (InitializerIndentFor(token) is not null)
            return false;

        if (_plan.LabelIndentation is not null && LabelFor(token) is { } label)
        {
            return _plan.LabelIndentation == LabelIndentationStyle.NoChange
                || _checkMalformedRegions && IsUnsafeOriginal(label);
        }

        if (_plan.IndentSwitchLabels is not null
            && token.Parent is SwitchLabelSyntax switchLabel
            && BeginsAt(switchLabel, token))
        {
            return _checkMalformedRegions && IsUnsafeOriginal(switchLabel);
        }

        if (_plan.IndentCaseContentsWhenBlock is not null
            && DirectSwitchSectionBlock(token) is { } blockSection)
        {
            return !CaseBlockSectionIsSafe(blockSection);
        }

        if (_plan.IndentCaseContents is not null
            && DirectSwitchSectionStatement(token) is { } statementSection)
        {
            return _checkMalformedRegions && IsUnsafeOriginal(statementSection);
        }

        if (_plan.IndentBraces is not null
            && (token.IsKind(SyntaxKind.OpenBraceToken) || token.IsKind(SyntaxKind.CloseBraceToken)))
        {
            return false;
        }

        if (_plan.IndentBlockContents is not null
            && ContinuationIndentFor(token) is not null)
        {
            return false;
        }

        return _plan.IndentBlockContents is null
            || token.IsKind(SyntaxKind.CloseBraceToken)
            || !StartsBlockContent(token);
    }

    string? ContinuationIndentFor(SyntaxToken token)
    {
        if (token.Parent is ElseClauseSyntax or CatchClauseSyntax or FinallyClauseSyntax or SwitchLabelSyntax
            || token.IsKind(SyntaxKind.WhileKeyword) && token.Parent is DoStatementSyntax
            || _indentation.DirectContentFor(token) is not { } content
            || BeginsAt(content, token)
            || token.Parent?.FirstAncestorOrSelf<StatementSyntax>() is { } nestedStatement
            && nestedStatement != content
            && BeginsAt(nestedStatement, token)
            || !ReferenceEquals(content.SyntaxTree, token.Parent?.SyntaxTree))
        {
            return null;
        }

        if (ContentIndentOf(content) is not { } contentIndent
            || TokenIndentOf(token) is not { } tokenIndent)
        {
            return null;
        }
        if (!_indentation.TryGet(content, out var emittedContentIndent))
            return null;
        if (!IsOriginalToken(token)
            && contentIndent.Length == 0
            && tokenIndent.StartsWith(emittedContentIndent, StringComparison.Ordinal))
        {
            return tokenIndent;
        }
        return IndentationModel.Rebase(contentIndent, emittedContentIndent, tokenIndent);
    }

    /// <summary>
    /// Whether <paramref name="node"/>, which contains <paramref name="token"/>, begins at it.
    /// </summary>
    /// <remarks>
    /// Equivalent to <c>node.GetFirstToken() == token</c>. The token being written answers from its
    /// recorded predecessor; anything else falls back to the walk.
    /// </remarks>
    bool BeginsAt(SyntaxNode node, SyntaxToken token) =>
        token == _current.Token ? _current.Begins(node) : node.GetFirstToken() == token;

    /// <summary>
    /// Source indentation where <paramref name="content"/> begins, remembering the last answer.
    /// </summary>
    /// <remarks>
    /// Consecutive tokens usually sit in the same statement, and resolving a node's first token
    /// walks its left spine, so one cached owner removes nearly every one of those walks.
    /// </remarks>
    string? ContentIndentOf(SyntaxNode content)
    {
        if (!ReferenceEquals(_lastContent, content))
        {
            _lastContent = content;
            _lastContentIndent = SourceIndentOf(content.GetFirstToken());
        }

        return _lastContentIndent;
    }

    string? TokenIndentOf(SyntaxToken token) =>
        token == _current.Token ? _sourceIndents[_index] : SourceIndentOf(token);

    bool IsOriginalToken(SyntaxToken token) =>
        (token == _current.Token ? _index : PieceOf(token)) is { } index && _pieces[index].IsOriginal;

    string? SourceIndentOf(SyntaxToken token) =>
        PieceOf(token) is { } index ? _sourceIndents[index] : null;

    string SourceIndentAt(SyntaxToken token) =>
        SourceIndentOf(token)
        ?? throw new KeyNotFoundException($"Token '{token}' is absent from the effective stream.");

    /// <summary>
    /// The stream position of a token the walk is not currently on, or null when it has none.
    /// </summary>
    int? PieceOf(SyntaxToken token)
    {
        if (_piecesByToken is null)
        {
            _piecesByToken = new(_pieces.Length);
            for (var index = 0; index < _pieces.Length; index++)
                _piecesByToken.TryAdd(_pieces[index].Token, index);
        }

        return _piecesByToken.TryGetValue(token, out var found) ? found : null;
    }

    void RememberContentIndents(SyntaxToken token)
    {
        if (_indentation.DirectContentFor(token) is { } content && BeginsAt(content, token))
            _indentation.Remember(content, _lineIndent);

        for (var node = token.Parent; node is not null && BeginsAt(node, token); node = node.Parent)
        {
            if (node is AnonymousFunctionExpressionSyntax)
                _indentation.Remember(node, _lineIndent);
        }
    }

    static string SourceIndent(string source, int position)
    {
        var start = position == 0 ? 0 : source.LastIndexOfAny(LineBreaks, position - 1) + 1;
        var end = start;
        while (end < source.Length && source[end] is ' ' or '\t')
            end++;
        return source[start..end];
    }

    bool StartsBlockContent(SyntaxToken token)
    {
        if (token.IsKind(SyntaxKind.OpenBraceToken)
            && token.Parent is BlockSyntax or AccessorListSyntax or BaseTypeDeclarationSyntax or BaseNamespaceDeclarationSyntax or SwitchStatementSyntax)
        {
            return true;
        }

        if (token.Parent?.FirstAncestorOrSelf<StatementSyntax>() is { } statement
            && BeginsAt(statement, token))
        {
            return true;
        }

        for (var node = token.Parent; node is not null && BeginsAt(node, token); node = node.Parent)
        {
            var parent = _indentation.ParentOf(node);
            if (node is MemberDeclarationSyntax && parent is BaseTypeDeclarationSyntax or BaseNamespaceDeclarationSyntax
                || node is AccessorDeclarationSyntax && parent is AccessorListSyntax
                || node is EnumMemberDeclarationSyntax && parent is EnumDeclarationSyntax
                || node is SwitchExpressionArmSyntax
                || node is SwitchLabelSyntax
                || node is AnonymousObjectMemberDeclaratorSyntax && parent is AnonymousObjectCreationExpressionSyntax
                || node is SubpatternSyntax && parent is PropertyPatternClauseSyntax
                || node is UsingDirectiveSyntax && parent is BaseNamespaceDeclarationSyntax
                || node is ExternAliasDirectiveSyntax && parent is BaseNamespaceDeclarationSyntax)
            {
                return true;
            }
        }

        return false;
    }

    static LabeledStatementSyntax? LabelFor(SyntaxToken token) =>
        token.Parent is LabeledStatementSyntax label && token == label.Identifier
            ? label
            : null;

    string RemoveIndentUnit(string indent) =>
        indent.EndsWith(_plan.IndentUnit, StringComparison.Ordinal)
            ? indent[..^_plan.IndentUnit.Length]
            : "";

    /// <summary>
    /// Tracks the indentation a brace opens and closes.
    /// </summary>
    /// <remarks>
    /// A brace is indented from the content around it and its own contents from the brace, which is
    /// what <c>csharp_indent_braces</c> and <c>csharp_indent_block_contents</c> each select.
    /// </remarks>
    void EnterOrLeave(SyntaxToken token)
    {
        if (InitializerIndentationRule.FindDelimiter(token) is { IsOpening: false } delimiter
            && _initializerFrames.TryPeek(out var frame)
            && ReferenceEquals(frame.Initializer, delimiter.Initializer))
        {
            _initializerFrames.Pop();
        }

        if (token.IsKind(SyntaxKind.OpenBraceToken))
        {
            var braceIndent = LambdaBlockIndentFor(token);
            braceIndent ??= token.Parent is SwitchExpressionSyntax
                ? _lineIndent
                : CaseBlockIndent(token)
                ?? (_plan.IndentBraces is { } indentBraces
                    ? BraceBaseIndent(token) + (indentBraces ? _plan.IndentUnit : "")
                    : _lineIndent);
            _braceIndents.Push(braceIndent);
            _indentation.RememberBrace(token.Parent!, braceIndent);
            _contentIndents.Add(_plan.IndentBlockContents is not null
                ? _indentation.Contents(token.Parent!)
                : braceIndent);
        }
        else if (token.IsKind(SyntaxKind.CloseBraceToken) && _contentIndents.Count > 1)
        {
            _contentIndents.RemoveAt(_contentIndents.Count - 1);
            _braceIndents.Pop();
        }
    }

    string? InitializerIndentFor(SyntaxToken token)
    {
        if (!_initializerFrames.TryPeek(out var frame))
            return null;

        if (InitializerIndentationRule.FindDelimiter(token) is { } delimiter
            && ReferenceEquals(frame.Initializer, delimiter.Initializer))
            return frame.Indent;

        if (_plan.IndentBlockContents is { } indentBlockContents
            && StartsDirectInitializerItem(frame.Initializer, token))
        {
            return frame.Indent + (indentBlockContents ? _plan.IndentUnit : "");
        }

        return null;
    }

    bool StartsDirectInitializerItem(SyntaxNode initializer, SyntaxToken token)
    {
        // Find the owning item from the token. Scanning every item for each token makes large
        // initializers quadratic, even though only one ancestor can be the direct item.
        for (var item = token.Parent; item is not null && item != initializer; item = item.Parent)
        {
            if (item.Parent != initializer)
                continue;

            return (initializer, item) is
                (AnonymousObjectCreationExpressionSyntax, AnonymousObjectMemberDeclaratorSyntax)
                or (InitializerExpressionSyntax, ExpressionSyntax)
                or (CollectionExpressionSyntax, CollectionElementSyntax)
                && BeginsAt(item, token);
        }

        return false;
    }

    bool? DesiredSpace(TokenPair pair, bool boundaryBeforeLeft, bool originalOccurrence)
    {
        if ((_plan.Trigger(pair.Left.RawKind) | _plan.Trigger(pair.Right.RawKind)) == 0
            || boundaryBeforeLeft
            || originalOccurrence
            && _checkMalformedRegions
            && _context.IsUnsafe(TextSpan.FromBounds(pair.Left.FullSpan.Start, pair.Right.FullSpan.End)))
        {
            return null;
        }

        return _plan.DesiredSpace(pair.Left, pair.Right);
    }

    bool? ClaimsBreak(SyntaxToken left, SyntaxToken token) =>
        _claims.Before(left, token, _lastTokenStartedLine);

    static SyntaxNode? SingleLineBraceOwner(SyntaxToken token, bool opening) =>
        ClaimedBreaks.SingleLineBraceOwner(token, opening);

    bool IsSafeSingleLine(SyntaxNode container) => _claims.IsSafeSingleLine(container);

    bool IsSafePreservationContainer(SyntaxNode container) => _claims.IsSafePreservationContainer(container);

    bool CanRewritePreservingTrivia(SyntaxNode node) => _claims.CanRewritePreservingTrivia(node);

    bool IsUnsafeOriginal(SyntaxNode node) => _claims.IsUnsafeOriginal(node);

    bool IsUnsafeOriginal(SyntaxToken token) => _claims.IsUnsafeOriginal(token);

    static bool InsideInterpolatedString(SyntaxToken token, bool afterToken)
    {
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            switch (node)
            {
                case InterpolatedStringExpressionSyntax interpolated:
                    if (token != (afterToken ? interpolated.GetLastToken() : interpolated.GetFirstToken()))
                        return true;
                    break;
                case StatementSyntax or MemberDeclarationSyntax:
                    return false;
            }
        }

        return false;
    }

    static bool IsWrappedExpressionOperator(SyntaxToken token) =>
        token.Parent is BinaryExpressionSyntax binary && token == binary.OperatorToken
        || token.Parent is ConditionalExpressionSyntax conditional
        && (token == conditional.QuestionToken || token == conditional.ColonToken);

    void Copy(string source, int start, int end)
    {
        if (end <= start)
            return;
        _output.Append(source, start, end - start);
        TrackCopiedText(source.AsSpan(start, end - start));
    }

    void TrackCopiedText(ReadOnlySpan<char> text)
    {
        var lastBreak = text.LastIndexOfAny('\n', '\r');
        if (lastBreak < 0)
        {
            if (_column == _lineIndent.Length)
                _lineIndent += text[..InitialIndentLength(text)].ToString();
            _column += text.Length;
            return;
        }

        _lineVersion++;

        var currentLine = text[(lastBreak + 1)..];
        _previousLineIndent = _lineIndent;
        _column = currentLine.Length;
        _lineIndent = currentLine[..InitialIndentLength(currentLine)].ToString();
    }

    static int InitialIndentLength(ReadOnlySpan<char> text)
    {
        var length = 0;
        while (length < text.Length && text[length] is ' ' or '\t')
            length++;
        return length;
    }

    void Append(string text)
    {
        _output.Append(text);
        TrackCopiedText(text);
    }

    void AppendTrivia(SyntaxTriviaList trivia)
    {
        foreach (var item in trivia)
        {
            var text = item.ToFullString();
            _output.Append(text);
            TrackCopiedText(text);
        }
    }

    static readonly char[] LineBreaks = ['\n', '\r'];
}
