using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static DressSharp.Rules.SyntaxWrappingRendering;
using WrappingMode = DressSharp.Rules.SyntaxWrappingSettings.WrappingMode;

namespace DressSharp.Rules;

sealed class SyntaxWrappingSolver
{
    readonly string _source;
    readonly EffectiveTokenStream _stream;
    readonly RuleSettings _settings;
    readonly EmitterPlan _emitterPlan;
    readonly string _lineEnding;
    readonly bool _needsWidths;
    readonly Occurrence[] _occurrences;
    readonly Boundary[] _boundaries;
    readonly int _skippedOccurrences;

    TriviaLayoutPlan? _trivia;
    SparseGapWidths? _plannedWidths;
    Dictionary<int, int>? _baseGapWidths;
    internal SyntaxWrappingSolver(
        string source,
        EffectiveTokenStream stream,
        SyntaxWrappingSettings.Setting?[] byKind,
        RuleSettings settings,
        EmitterPlan emitterPlan,
        RuleContext context,
        SyntaxWrappingDiscovery.Result discovery)
    {
        _source = source;
        _stream = stream;
        _settings = settings;
        _emitterPlan = emitterPlan;
        _lineEnding = context.LineEnding;
        _occurrences = discovery.Occurrences;
        _boundaries = discovery.Boundaries;
        _skippedOccurrences = discovery.SkippedOccurrences;

        foreach (var setting in byKind)
        {
            _needsWidths |= setting is
                        { Mode: WrappingMode.Auto, MaximumLineLength: not int.MaxValue };
        }
    }
    internal SyntaxWrappingPlan Finish(TriviaLayoutPlan trivia)
    {
        _trivia = trivia;
        if (_needsWidths)
            _plannedWidths = new(_boundaries);
        Decide();
        var gapMap = new Dictionary<int, string>(_boundaries.Length);
        for (var occurrenceIndex = _occurrences.Length - 1; occurrenceIndex >= 0; occurrenceIndex--)
        {
            var occurrence = _occurrences[occurrenceIndex];
            if (!occurrence.Applies)
                continue;

            var indent = occurrence.Multi ? Indent(occurrenceIndex, 1) : "";
            var baseIndent = occurrence.Multi ? Indent(occurrenceIndex, 0) : "";
            for (var offset = 0; offset < occurrence.BoundaryCount; offset++)
            {
                var boundary = _boundaries[occurrence.BoundaryStart + offset];
                gapMap[boundary.RightIndex] = Render(
                    boundary,
                    _trivia!,
                    occurrence.Multi,
                    indent,
                    baseIndent,
                    _lineEnding);
            }
        }

        var gaps = gapMap.Count == 0
            ? []
            : new string?[_stream.Pieces.Length];
        foreach (var (tokenIndex, text) in gapMap)
            gaps[tokenIndex] = text;
        return new(gaps, _skippedOccurrences);
    }
    void Decide()
    {
        for (var occurrenceIndex = _occurrences.Length - 1; occurrenceIndex >= 0; occurrenceIndex--)
        {
            var occurrence = _occurrences[occurrenceIndex];
            var multi = occurrence.Setting.Mode == WrappingMode.Multi;
            var hasLineComment = false;
            var checkedLineComments = false;
            if (occurrence.Setting is
                    { Mode: WrappingMode.Auto, MaximumLineLength: not int.MaxValue })
            {
                var measurement = Measure(occurrence);
                var width = measurement.Width;
                hasLineComment = measurement.HasLineComment;
                checkedLineComments = true;
                for (var offset = 0; offset < occurrence.BoundaryCount; offset++)
                {
                    var boundary = _boundaries[occurrence.BoundaryStart + offset];
                    width += PlannedWidth(
                            boundary,
                            _trivia!,
                            false,
                            _lineEnding,
                            _settings.TabWidth)
                        - CurrentGapWidth(boundary.RightIndex);
                }

                multi = (long)VisualStartColumn(occurrence) + width
                    > occurrence.Setting.MaximumLineLength;
            }

            if (!multi
                && (hasLineComment || !checkedLineComments && HasLineComment(occurrence)))
                continue;

            occurrence = occurrence with { Applies = true, Multi = multi };
            _occurrences[occurrenceIndex] = occurrence;
            if (_plannedWidths is null)
                continue;
            for (var offset = 0; offset < occurrence.BoundaryCount; offset++)
            {
                var boundary = _boundaries[occurrence.BoundaryStart + offset];
                _plannedWidths.Set(
                    boundary.RightIndex,
                    PlannedWidth(
                        boundary,
                        _trivia!,
                        multi,
                        _lineEnding,
                        _settings.TabWidth));
            }
        }
    }

    Measurement Measure(Occurrence occurrence)
    {
        var pieces = _stream.Pieces;
        var cursor = _plannedWidths!.Cursor(occurrence.FirstToken + 1);
        var width = 0;
        var hasLineComment = false;
        for (var index = occurrence.FirstToken; index <= occurrence.LastToken; index++)
        {
            if (index != occurrence.FirstToken)
            {
                width += _plannedWidths.TryGetAt(index, ref cursor, out var planned)
                    ? planned
                    : BaseGapWidth(index);
            }

            width += VisualWidth(pieces[index].Token.Text, _settings.TabWidth);
            hasLineComment |= ContainsLineComment(_trivia!.Leading(index))
                || ContainsLineComment(_trivia.Trailing(index));
        }

        return new(width, hasLineComment);
    }

    int CurrentGapWidth(int index) =>
        _plannedWidths!.TryGet(index, out var planned) ? planned : BaseGapWidth(index);

    int BaseGapWidth(int index)
    {
        if (_baseGapWidths?.TryGetValue(index, out var width) == true)
            return width;
        width = SyntaxWrappingRendering.BaseGapWidth(
            _stream.Pieces[index - 1].Token,
            _stream.Pieces[index].Token,
            _trivia!.Trailing(index - 1),
            _trivia.Leading(index),
            _settings.TabWidth,
            _emitterPlan);
        (_baseGapWidths ??= [])[index] = width;
        return width;
    }

    bool HasLineComment(Occurrence occurrence)
    {
        for (var index = occurrence.FirstToken; index <= occurrence.LastToken; index++)
        {
            if (ContainsLineComment(_trivia!.Leading(index))
                || ContainsLineComment(_trivia.Trailing(index)))
            {
                return true;
            }
        }

        return false;
    }

    int VisualStartColumn(Occurrence occurrence)
    {
        var pieces = _stream.Pieces;
        var piece = pieces[occurrence.FirstToken];
        var localLineStart = LastLineStart(piece.Source, piece.Token.SpanStart);
        int first;
        int column;
        if (!piece.IsOriginal && localLineStart != 0)
        {
            first = occurrence.FirstToken;
            while (first > 0
                && pieces[first - 1].SegmentIndex == piece.SegmentIndex
                && pieces[first - 1].Token.SpanStart >= localLineStart)
            {
                first--;
            }

            column = VisualWidth(
                piece.Source,
                localLineStart,
                pieces[first].Token.SpanStart,
                _settings.TabWidth,
                0);
        }
        else
        {
            var originalLineStart = LastLineStart(_source, piece.OriginalPosition);
            first = occurrence.FirstToken;
            while (first > 0 && pieces[first - 1].OriginalPosition >= originalLineStart)
                first--;

            var firstPiece = pieces[first];
            if (firstPiece.IsOriginal)
            {
                column = VisualWidth(
                    _source,
                    originalLineStart,
                    firstPiece.Token.SpanStart,
                    _settings.TabWidth,
                    0);
            }
            else if (_stream.TryGetReplacementSegment(
                firstPiece.SegmentIndex,
                out var segment))
            {
                column = VisualWidth(
                    _source,
                    originalLineStart,
                    segment.Original.Start,
                    _settings.TabWidth,
                    0);
                column = VisualWidth(
                    firstPiece.Source,
                    0,
                    firstPiece.Token.SpanStart,
                    _settings.TabWidth,
                    column);
            }
            else
            {
                throw new InvalidOperationException(
                    "Replacement token has no effective stream segment.");
            }
        }

        for (var index = first; index < occurrence.FirstToken; index++)
        {
            column = AdvanceColumn(pieces[index].Token.Text, column, _settings.TabWidth);
            column = AdvancePrefixGap(index + 1, column);
        }

        return column;
    }

    int AdvancePrefixGap(int rightIndex, int column)
    {
        if (_plannedWidths?.TryGet(rightIndex, out var planned) == true)
            return column + planned;

        var trailing = _trivia!.Trailing(rightIndex - 1);
        var leading = _trivia.Leading(rightIndex);
        if (_trivia.HasMeaningfulGap(rightIndex) || _trivia.HasLineBreak(rightIndex))
            return AdvanceColumn(leading, AdvanceColumn(trailing, column));

        var left = _stream.Pieces[rightIndex - 1].Token;
        var right = _stream.Pieces[rightIndex].Token;
        return _emitterPlan.DesiredSpace(left, right) switch
            {
                true => column + 1,
                false => column,
                null => AdvanceColumn(leading, AdvanceColumn(trailing, column))
            };
    }

    int AdvanceColumn(SyntaxTriviaList trivia, int column)
    {
        foreach (var item in trivia)
            column = AdvanceColumn(item.ToFullString(), column, _settings.TabWidth);
        return column;
    }

    static int AdvanceColumn(string text, int column, int tabWidth)
    {
        foreach (var character in text)
        {
            column = character switch
                {
                    '\r' or '\n' => 0,
                    '\t' => column + tabWidth - column % tabWidth,
                    _ => column + 1
                };
        }

        return column;
    }

    static int LastLineStart(string source, int position)
    {
        if (position == 0)
            return 0;
        var carriageReturn = source.LastIndexOf('\r', position - 1);
        var lineFeed = source.LastIndexOf('\n', position - 1);
        return Math.Max(carriageReturn, lineFeed) + 1;
    }

    string Indent(int occurrenceIndex, int extra)
    {
        var occurrence = _occurrences[occurrenceIndex];
        var node = occurrence.Node;
        var position = node is InitializerExpressionSyntax or CollectionExpressionSyntax
            ? node.GetFirstToken().GetPreviousToken().SpanStart
            : node.SpanStart;
        var text = node.SyntaxTree.GetText();
        var line = text.Lines.GetLineFromPosition(position);
        var leading = 0;
        while (line.Start + leading < line.End
            && char.IsWhiteSpace(text[line.Start + leading]))
        {
            leading++;
        }

        var units = extra + MultilineLayoutAncestors(occurrenceIndex);
        var unit = _settings.IndentUnit;
        return string.Create(
            leading + units * unit.Length,
            (text, line.Start, leading, unit, units),
            static (destination, state) =>
                {
                    for (var index = 0; index < state.leading; index++)
                        destination[index] = state.text[state.Start + index];
                    var position = state.leading;
                    for (var count = 0; count < state.units; count++)
                    {
                        state.unit.AsSpan().CopyTo(destination[position..]);
                        position += state.unit.Length;
                    }
                });
    }

    int MultilineLayoutAncestors(int occurrenceIndex)
    {
        var count = 0;
        var occurrence = _occurrences[occurrenceIndex];
        for (var parentIndex = occurrence.Parent; parentIndex >= 0;)
        {
            var parent = _occurrences[parentIndex];
            if (!StartsOnSameLine(occurrence.Node, parent.Node))
                break;
            if (parent is { Applies: true, Multi: true })
                count++;
            parentIndex = parent.Parent;
        }

        return count;
    }

    static bool StartsOnSameLine(SyntaxNode node, SyntaxNode ancestor)
    {
        if (!ReferenceEquals(node.SyntaxTree, ancestor.SyntaxTree))
            return false;
        var text = node.SyntaxTree.GetText();
        return text.Lines.GetLineFromPosition(node.SpanStart).LineNumber
            == text.Lines.GetLineFromPosition(ancestor.SpanStart).LineNumber;
    }

    readonly record struct Measurement(int Width, bool HasLineComment);
}

sealed class SparseGapWidths
{
    readonly Entry[] _entries;

        internal SparseGapWidths(IReadOnlyList<Boundary> boundaries)
    {
        var entries = new Entry[boundaries.Count];
        for (var index = 0; index < boundaries.Count; index++)
            entries[index] = new(boundaries[index].RightIndex, -1);
        Array.Sort(entries, static (left, right) => left.Index.CompareTo(right.Index));

        var unique = 0;
        for (var index = 0; index < entries.Length; index++)
        {
            if (unique != 0 && entries[unique - 1].Index == entries[index].Index)
                continue;
            entries[unique++] = entries[index];
        }

        if (unique != entries.Length)
            Array.Resize(ref entries, unique);
        _entries = entries;
    }

    internal int Cursor(int index) => LowerBound(index);

    internal bool TryGetAt(int index, ref int cursor, out int width)
    {
        while (cursor < _entries.Length && _entries[cursor].Index < index)
            cursor++;
        if (cursor < _entries.Length
            && _entries[cursor] is { Index: var found, Width: >= 0 }
            && found == index)
        {
            width = _entries[cursor].Width;
            return true;
        }

        width = 0;
        return false;
    }

    internal bool TryGet(int index, out int width)
    {
        var found = LowerBound(index);
        if (found < _entries.Length
            && _entries[found] is { Index: var entryIndex, Width: >= 0 }
            && entryIndex == index)
        {
            width = _entries[found].Width;
            return true;
        }

        width = 0;
        return false;
    }

    internal void Set(int index, int width)
    {
        var found = LowerBound(index);
        if (found >= _entries.Length || _entries[found].Index != index)
            throw new InvalidOperationException($"Gap '{index}' is absent from the sparse plan.");
        _entries[found].Width = width;
    }

    int LowerBound(int index)
    {
        var low = 0;
        var high = _entries.Length;
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            if (_entries[middle].Index < index)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    struct Entry(int index, int width)
    {
        internal int Index { get; } = index;
        internal int Width { get; set; } = width;
    }
}
