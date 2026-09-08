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
    readonly Dictionary<int, bool> _chainBoundaryBreaks = [];
    readonly Dictionary<int, string> _plannedLineBreaks = [];
    int[]? _braceDepths;
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
                        { Mode: WrappingMode.Auto or WrappingMode.Compact, MaximumLineLength: not int.MaxValue };
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
            var ladder = occurrence.Multi && CanUseDecisionLadder(occurrence, gapMap);
            for (var offset = 0; offset < occurrence.BoundaryCount; offset++)
            {
                var boundary = _boundaries[occurrence.BoundaryStart + offset];
                if (boundary.RightIndex == occurrence.FirstToken && !boundary.BreakWhenMulti && !ladder)
                    continue;
                var multi = BoundaryBreak(occurrence, offset);
                var boundaryIndent = indent;
                if (occurrence.Multi && occurrence.Setting.NestedStyle is { } style)
                {
                    if (ladder)
                    {
                        boundaryIndent = Indent(occurrenceIndex,
                            _emitterPlan.IndentBlockContents != true && _trivia!.HasLineBreak(occurrence.FirstToken) ? 0 : 1);
                        multi = boundary.RightIndex == occurrence.FirstToken
                            || boundary.BreakWhenMulti && boundary.OperatorIndex >= 0
                            && _stream.Pieces[boundary.OperatorIndex].Token.IsKind(SyntaxKind.ColonToken);
                    }
                    else if (style != "flat" && boundary.OperatorIndex >= 0)
                    {
                        var expression = _stream.Pieces[boundary.OperatorIndex].Token.Parent!;
                        var depth = 0;
                        for (var parent = expression.Parent; parent is not null && expression != occurrence.Node; parent = parent.Parent)
                        {
                            if (parent is ConditionalExpressionSyntax)
                                depth++;
                            if (parent == occurrence.Node)
                                break;
                        }
                        boundaryIndent = Indent(occurrenceIndex, 1 + depth);
                    }
                }
                gapMap[boundary.RightIndex] = Render(
                    boundary,
                    _trivia!,
                    multi,
                    boundaryIndent,
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
        for (var occurrenceIndex = 0; occurrenceIndex < _occurrences.Length; occurrenceIndex++)
        {
            var occurrence = _occurrences[occurrenceIndex];
            var initializerLayout = SyntaxWrappingRule.InitializerKindFor(occurrence.Kind) is not null;
            var hasLineBreak = HasLineBreak(occurrence);
            var hasNestedLineBreak = initializerLayout && HasUnownedLineBreak(occurrence);
            if (occurrence.Setting.ShapesOperators && HasCommentedOperator(occurrence))
                continue;
            if (occurrence.Setting.Mode == WrappingMode.Preserve && !hasLineBreak)
                continue;
            if (occurrence.Setting.Mode == WrappingMode.Auto
                && (hasLineBreak || hasNestedLineBreak)
                && !initializerLayout && !occurrence.Setting.ShapesOperators)
                continue;

            var multi = occurrence.Setting.Mode == WrappingMode.Multi
                || occurrence.Setting.ShapesOperators && hasLineBreak
                && occurrence.Setting.Mode is WrappingMode.Auto or WrappingMode.Preserve
                || occurrence.Setting.Mode == WrappingMode.Auto
                && initializerLayout
                && (hasLineBreak || hasNestedLineBreak)
                || occurrence.Setting.Mode == WrappingMode.Compact
                && initializerLayout
                && hasNestedLineBreak;
            var hasLineComment = false;
            var checkedLineComments = false;
            if (!multi
                && occurrence.Setting is
                    { Mode: WrappingMode.Auto or WrappingMode.Compact, MaximumLineLength: not int.MaxValue })
            {
                if (occurrence.Node is MemberAccessExpressionSyntax or ConditionalAccessExpressionSyntax or BaseArgumentListSyntax
                    && MeasureMultilineBoundaries(occurrence) is { } breaks)
                {
                    multi = breaks.Any(value => value);
                    if (occurrence.Node is MemberAccessExpressionSyntax or ConditionalAccessExpressionSyntax)
                    {
                        for (var offset = 0; offset < breaks.Length; offset++)
                            _chainBoundaryBreaks[occurrence.BoundaryStart + offset] = breaks[offset];
                    }
                }
                else
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
            }

            if (!multi
                && (hasLineComment || !checkedLineComments && HasLineComment(occurrence)))
            {
                if (initializerLayout && occurrence.Setting.Mode == WrappingMode.Compact)
                    multi = true;
                else
                    continue;
            }

            occurrence = occurrence with { Applies = true, Multi = multi };
            _occurrences[occurrenceIndex] = occurrence;
            if (_plannedWidths is null)
                continue;
            for (var offset = 0; offset < occurrence.BoundaryCount; offset++)
            {
                var boundary = _boundaries[occurrence.BoundaryStart + offset];
                var boundaryMulti = BoundaryBreak(occurrence, offset);
                if (boundaryMulti)
                {
                    _plannedLineBreaks[boundary.RightIndex] = Render(
                        boundary, _trivia!, true, Indent(occurrenceIndex, 1), Indent(occurrenceIndex, 0), _lineEnding);
                }
                else
                    _plannedLineBreaks.Remove(boundary.RightIndex);
                _plannedWidths.Set(
                    boundary.RightIndex,
                    PlannedWidth(
                        boundary,
                        _trivia!,
                        boundaryMulti,
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

    bool BoundaryBreak(Occurrence occurrence, int offset)
    {
        var boundary = _boundaries[occurrence.BoundaryStart + offset];
        if (!boundary.BreakWhenMulti)
            return false;
        if (occurrence.Setting.Mode == WrappingMode.Preserve
            && occurrence.Setting.NestedStyle is null && boundary.OperatorIndex >= 0)
            return _trivia!.HasLineBreak(boundary.OperatorIndex) || _trivia.HasLineBreak(boundary.OperatorIndex + 1);
        return _chainBoundaryBreaks.GetValueOrDefault(occurrence.BoundaryStart + offset, occurrence.Multi);
    }

    bool HasCommentedOperator(Occurrence occurrence)
    {
        for (var offset = 0; offset < occurrence.BoundaryCount; offset++)
        {
            var boundary = _boundaries[occurrence.BoundaryStart + offset];
            if (boundary.OperatorIndex >= 0 && _trivia!.HasMeaningfulGap(boundary.RightIndex))
                return true;
        }
        return false;
    }

    bool CanUseDecisionLadder(Occurrence occurrence, IReadOnlyDictionary<int, string> childGaps)
    {
        if (occurrence.Setting.NestedStyle != "decision_ladder"
            || occurrence.Node is not ConditionalExpressionSyntax { WhenFalse: ConditionalExpressionSyntax } root)
            return false;

        // Only the false-branch spine can become a ladder. A nested true branch remains a tree.
        for (var expression = root; ;)
        {
            if (expression.Condition.DescendantNodesAndSelf().OfType<ConditionalExpressionSyntax>().Any()
                || expression.WhenTrue.DescendantNodesAndSelf().OfType<ConditionalExpressionSyntax>().Any())
                return false;
            if (expression.WhenFalse is not ConditionalExpressionSyntax next)
                break;
            expression = next;
        }

        var owned = new HashSet<int>();
        for (var offset = 0; offset < occurrence.BoundaryCount; offset++)
            owned.Add(_boundaries[occurrence.BoundaryStart + offset].RightIndex);
        for (var index = occurrence.FirstToken; index <= occurrence.LastToken; index++)
        {
            if (_stream.Pieces[index].Token.Text.Contains('\n') || _stream.Pieces[index].Token.Text.Contains('\r'))
                return false;
            if (index > occurrence.FirstToken && !owned.Contains(index)
                && (childGaps.TryGetValue(index, out var gap)
                    ? gap.Contains('\n') || gap.Contains('\r')
                    : _trivia!.HasLineBreak(index)))
                return false;
        }
        return true;
    }

    // Only lines containing this occurrence's wrapping boundaries can benefit from wrapping it.
    // Nested argument and lambda-body lines must not contribute to another line's width.
    bool[]? MeasureMultilineBoundaries(Occurrence occurrence)
    {
        var lineWidths = new List<int>();
        var boundaryLines = new int[occurrence.BoundaryCount];
        var column = VisualStartColumn(occurrence);
        var offset = 0;
        for (var index = occurrence.FirstToken; index <= occurrence.LastToken; index++)
        {
            if (index != occurrence.FirstToken)
            {
                if (_plannedLineBreaks.TryGetValue(index, out var gap))
                    Advance(gap);
                else if (_plannedWidths!.TryGet(index, out var width))
                    column += width;
                else if (_trivia!.HasLineBreak(index) || _trivia.HasMeaningfulGap(index))
                {
                    foreach (var item in _trivia.Trailing(index - 1))
                        Advance(item.ToFullString());
                    foreach (var item in _trivia.Leading(index))
                        Advance(item.ToFullString());
                }
                else
                    column += BaseGapWidth(index);
            }

            if (offset < boundaryLines.Length
                && _boundaries[occurrence.BoundaryStart + offset].RightIndex == index)
                boundaryLines[offset++] = lineWidths.Count;
            Advance(_stream.Pieces[index].Token.Text);
        }

        if (lineWidths.Count == 0)
            return null;
        lineWidths.Add(column);
        return boundaryLines.Select(line => lineWidths[line] > occurrence.Setting.MaximumLineLength).ToArray();

        void Advance(string text)
        {
            foreach (var character in text)
            {
                if (character is '\r' or '\n')
                {
                    lineWidths.Add(column);
                    column = 0;
                }
                else
                    column += character == '\t' ? _settings.TabWidth - column % _settings.TabWidth : 1;
            }
        }
    }

    bool HasLineBreak(Occurrence occurrence)
    {
        for (var offset = 0; offset < occurrence.BoundaryCount; offset++)
        {
            var boundary = _boundaries[occurrence.BoundaryStart + offset];
            if ((boundary.RightIndex != occurrence.FirstToken || boundary.BreakWhenMulti)
                && _trivia!.HasLineBreak(boundary.RightIndex))
                return true;
        }

        return false;
    }

    bool HasUnownedLineBreak(Occurrence occurrence)
    {
        for (var index = occurrence.FirstToken; index <= occurrence.LastToken; index++)
        {
            if (!_trivia!.HasLineBreak(index))
                continue;

            var owned = false;
            for (var offset = 0; offset < occurrence.BoundaryCount; offset++)
            {
                if (_boundaries[occurrence.BoundaryStart + offset].RightIndex != index)
                    continue;
                owned = true;
                break;
            }

            if (!owned)
                return true;
        }

        return false;
    }

    int VisualStartColumn(Occurrence occurrence)
    {
        if (occurrence.Node is InitializerExpressionSyntax
            && occurrence.FirstToken > 0)
        {
            var previous = occurrence.FirstToken - 1;
            return VisualStartColumn(previous)
                + VisualWidth(_stream.Pieces[previous].Token.Text, _settings.TabWidth)
                + 1;
        }

        return VisualStartColumn(occurrence.FirstToken);
    }

    int VisualStartColumn(int firstToken)
    {
        var pieces = _stream.Pieces;
        var piece = pieces[firstToken];
        var localLineStart = LastLineStart(piece.Source, piece.Token.SpanStart);
        int first;
        int column;
        if (!piece.IsOriginal && localLineStart != 0)
        {
            first = firstToken;
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
            first = firstToken;
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

        for (var index = first; index < firstToken; index++)
        {
            column = AdvanceColumn(pieces[index].Token.Text, column, _settings.TabWidth);
            column = AdvancePrefixGap(index + 1, column);
        }

        return column;
    }

    int AdvancePrefixGap(int rightIndex, int column)
    {
        if (_plannedLineBreaks.TryGetValue(rightIndex, out var plannedBreak))
            return AdvanceColumn(plannedBreak, column, _settings.TabWidth);

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
        var units = extra + MultilineLayoutAncestors(occurrenceIndex);
        var unit = _settings.IndentUnit;
        if (_emitterPlan.IndentBlockContents == true)
        {
            var blockDepth = BraceDepth(occurrence.FirstToken);
            return string.Concat(Enumerable.Repeat(unit, blockDepth + units));
        }

        var pieceIndex = node is InitializerExpressionSyntax or CollectionExpressionSyntax
            ? Math.Max(0, occurrence.FirstToken - 1)
            : occurrence.FirstToken;
        var piece = _stream.Pieces[pieceIndex];
        var position = piece.Token.SpanStart;
        var text = Microsoft.CodeAnalysis.Text.SourceText.From(piece.Source);
        var line = text.Lines.GetLineFromPosition(position);
        var leading = 0;
        while (line.Start + leading < line.End
            && char.IsWhiteSpace(text[line.Start + leading]))
        {
            leading++;
        }

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

    int BraceDepth(int tokenIndex)
    {
        if (_braceDepths is null)
        {
            _braceDepths = new int[_stream.Pieces.Length];
            var depth = 0;
            for (var index = 0; index < _stream.Pieces.Length; index++)
            {
                _braceDepths[index] = depth;
                var token = _stream.Pieces[index].Token;
                if (token.IsKind(SyntaxKind.OpenBraceToken))
                    depth++;
                else if (token.IsKind(SyntaxKind.CloseBraceToken))
                    depth--;
            }
        }

        return _braceDepths[tokenIndex];
    }

    int MultilineLayoutAncestors(int occurrenceIndex)
    {
        var count = 0;
        var occurrence = _occurrences[occurrenceIndex];
        for (var parentIndex = occurrence.Parent; parentIndex >= 0;)
        {
            var parent = _occurrences[parentIndex];
            if (_emitterPlan.IndentBlockContents != true
                && !StartsOnSameLine(occurrence.Node, parent.Node))
            {
                break;
            }
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
