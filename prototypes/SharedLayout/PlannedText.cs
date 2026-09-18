using System.Text;

namespace DressSharp.Rules;

// PROTOTYPE: resolved fragments retain source slices. No complete candidate string or syntax tree
// is needed to ask layout questions. Production decisions are generated from SinglePassEmitter.
sealed class PlannedText(EffectiveTokenStream.Piece[] pieces)
{
    readonly List<TextFragment> _fragments = [];
    readonly TokenExtent[] _tokens = new TokenExtent[pieces.Length];
    int _length;
    int _line;
    bool _afterCarriageReturn;
    bool _insideToken;
    bool _commentBoundaryHazard;
    GapState _gapState;
    int _segment = -2;
    int _segmentStart;
    int _lastToken = -1;
    Dictionary<int, string>? _changedTokenText;

    // The emitter copies a comment gap, reads it back, and may cut it off again to write it moved.
    // Cutting happens only inside a gap, so the line count and comment state are replayed from
    // the end of the last token written, where both are known.
    internal int Length
    {
        get => _length;
        set
        {
            if (value > _length)
                throw new InvalidOperationException("Planned text can only be cut back.");
            while (_fragments.Count > 0)
            {
                var last = _fragments[^1];
                var fragmentStart = _length - last.Length;
                if (fragmentStart >= value)
                {
                    _fragments.RemoveAt(_fragments.Count - 1);
                    _length = fragmentStart;
                    continue;
                }

                _fragments[^1] = last with { Length = value - fragmentStart };
                _length = value;
                break;
            }

            var replayFrom = 0;
            _line = 0;
            if (_lastToken >= 0)
            {
                replayFrom = _tokens[_lastToken].End;
                _line = _tokens[_lastToken].EndLine;
            }

            _afterCarriageReturn = false;
            _gapState = GapState.Normal;
            foreach (var character in ToString(replayFrom, _length - replayFrom))
                Count(character);
        }
    }

    internal string ToString(int start, int count)
    {
        if (count == 0)
            return "";
        var index = _fragments.Count - 1;
        var fragmentStart = _length - _fragments[index].Length;
        while (fragmentStart > start)
        {
            index--;
            fragmentStart -= _fragments[index].Length;
        }

        var output = new StringBuilder(count);
        var position = start;
        var end = start + count;
        while (position < end)
        {
            var fragment = _fragments[index];
            var within = position - fragmentStart;
            var length = Math.Min(fragment.Length - within, end - position);
            output.Append(fragment.Source, fragment.Start + within, length);
            position += length;
            fragmentStart += fragment.Length;
            index++;
        }

        return output.ToString();
    }

    internal void Append(string text) => Append(text, 0, text.Length);

    internal void Append(string text, int start, int length)
    {
        if (length == 0)
            return;
        if (_fragments.Count > 0 && _fragments[^1] is var previous
            && ReferenceEquals(previous.Source, text) && previous.Start + previous.Length == start)
            _fragments[^1] = previous with { Length = previous.Length + length };
        else
            _fragments.Add(new(text, start, length));
        _length += length;
        var span = text.AsSpan(start, length);
        foreach (var character in span)
            Count(character);
    }

    void Count(char character)
    {
        if (character is '\r' or '\u0085' or '\u2028' or '\u2029' || character == '\n' && !_afterCarriageReturn)
            _line++;
        _afterCarriageReturn = character == '\r';
        if (!_insideToken)
            TrackGap(character);
    }

    internal void AppendToken(int index, string text)
    {
        var piece = pieces[index];
        if (_gapState == GapState.LineComment && pieces[index].Token.Span.Length != 0)
            _commentBoundaryHazard = true;
        _insideToken = true;
        _lastToken = index;
        _tokens[index] = new(_length, _length, _line, _line);
        if (_segment != piece.SegmentIndex)
        {
            _segment = piece.SegmentIndex;
            _segmentStart = piece.Token.FullSpan.Start;
        }
        if (text == piece.Token.Text)
            Append(piece.Source, piece.Token.SpanStart - (piece.IsOriginal ? 0 : _segmentStart), text.Length);
        else
        {
            (_changedTokenText ??= [])[index] = text;
            Append(text);
        }
        _tokens[index] = _tokens[index] with { End = _length, EndLine = _line };
        _insideToken = false;
        _gapState = GapState.Normal;
    }

    // Conservative prototype guard: a planned token cannot remain active after an unterminated
    // line comment. Preserve the old parse path for this known class of lexical changes.
    void TrackGap(char character)
    {
        _gapState = _gapState switch
        {
            GapState.LineComment => character is '\r' or '\n' or '\u0085' or '\u2028' or '\u2029' ? GapState.Normal : GapState.LineComment,
            GapState.BlockComment => character == '*' ? GapState.BlockStar : GapState.BlockComment,
            GapState.BlockStar => character == '/' ? GapState.Normal : character == '*' ? GapState.BlockStar : GapState.BlockComment,
            GapState.Slash => character == '/' ? GapState.LineComment : character == '*' ? GapState.BlockComment : GapState.Normal,
            _ => character == '/' ? GapState.Slash : GapState.Normal
        };
    }

    internal ResolvedTokenLayout Finish() => new(pieces, _fragments, _tokens, _length, _commentBoundaryHazard, _changedTokenText);

    enum GapState { Normal, Slash, LineComment, BlockComment, BlockStar }
}

readonly record struct TextFragment(string Source, int Start, int Length);
readonly record struct TokenExtent(int Start, int End, int StartLine, int EndLine);

sealed class ResolvedTokenLayout(
    EffectiveTokenStream.Piece[] pieces,
    List<TextFragment> fragments,
    TokenExtent[] tokens,
    int length,
    bool hasCommentBoundaryHazard,
    Dictionary<int, string>? changedTokenText = null)
{
    int[]? _fragmentOffsets;
    Dictionary<int, string>? _lineIndents;
    internal int Length => length;
    internal ResolvedTokenLayout Rebind(EffectiveTokenStream stream)
    {
        if (stream.Pieces.Length != pieces.Length) throw new InvalidOperationException("Rebinding changed token count");
        return new(stream.Pieces, fragments, tokens, length, hasCommentBoundaryHazard, changedTokenText);
    }
    internal bool HasCommentBoundaryHazard => hasCommentBoundaryHazard;
    internal EffectiveTokenStream.Piece[] Pieces => pieces;
    internal TokenExtent Extent(int index) => tokens[index];
    internal string TokenText(int index) => changedTokenText?.GetValueOrDefault(index) ?? pieces[index].Token.Text;
    internal bool IsMultiline(int first, int last) => tokens[first].StartLine != tokens[last].EndLine;

    internal string Slice(int start, int end)
    {
        if (end == start) return "";
        _fragmentOffsets ??= FragmentOffsets();
        var found = Array.BinarySearch(_fragmentOffsets, start);
        var index = found >= 0 ? found : ~found - 1;
        var output = new StringBuilder(end - start);
        while (start < end)
        {
            var fragment = fragments[index];
            var within = start - _fragmentOffsets[index];
            var count = Math.Min(fragment.Length - within, end - start);
            output.Append(fragment.Source, fragment.Start + within, count);
            start += count;
            index++;
        }
        return output.ToString();
    }

    internal string LeadingIndent(int tokenIndex)
    {
        _lineIndents ??= [];
        var line = tokens[tokenIndex].StartLine;
        if (_lineIndents.TryGetValue(line, out var cached)) return cached;
        _fragmentOffsets ??= FragmentOffsets();
        var position = tokens[tokenIndex].Start;
        var found = Array.BinarySearch(_fragmentOffsets, position);
        var index = found >= 0 ? Math.Min(found, fragments.Count - 1) : ~found - 1;
        var lineStart = 0;
        for (; index >= 0; index--)
        {
            var fragment = fragments[index];
            var count = Math.Min(fragment.Length, position - _fragmentOffsets[index]);
            var newline = fragment.Source.AsSpan(fragment.Start, count).LastIndexOfAny("\r\n\u0085\u2028\u2029");
            if (newline < 0) continue;
            lineStart = _fragmentOffsets[index] + newline + 1;
            break;
        }
        var prefix = Slice(lineStart, position);
        var indent = 0;
        while (indent < prefix.Length && prefix[indent] is ' ' or '\t') indent++;
        return _lineIndents[line] = prefix[..indent];
    }

    int[] FragmentOffsets()
    {
        var offsets = new int[fragments.Count + 1];
        for (var index = 0; index < fragments.Count; index++) offsets[index + 1] = offsets[index] + fragments[index].Length;
        return offsets;
    }

    // The final writer has no syntax or formatting decisions.
    internal string Render()
    {
        var output = new StringBuilder(length);
        foreach (var fragment in fragments)
            output.Append(fragment.Source, fragment.Start, fragment.Length);
        return output.ToString();
    }
}
