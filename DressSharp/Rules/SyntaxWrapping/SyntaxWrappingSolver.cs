using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static DressSharp.Rules.SyntaxWrappingRendering;
using WrappingMode = DressSharp.Rules.SyntaxWrappingSettings.WrappingMode;

namespace DressSharp.Rules;

sealed class SyntaxWrappingSolver : IWrappedItems
{
    readonly string _source;
    readonly EffectiveTokenStream _stream;
    readonly RuleSettings _settings;
    readonly EmitterPlan _emitterPlan;
    readonly IndentationModel _indentation;
    readonly ClaimedBreaks _claims;
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
    readonly Dictionary<int, string> _plannedBases = [];
    readonly HashSet<int> _settledBreaks = [];
    readonly Dictionary<int, bool> _settledBoundaries = [];
    Dictionary<SyntaxNode, int>? _occurrencesByNode;
    Dictionary<SyntaxToken, int>? _chainOperators;
    readonly Dictionary<int, string?> _lineIndents = [];

    internal SyntaxWrappingSolver(
        string source,
        EffectiveTokenStream stream,
        SyntaxWrappingSettings.Setting?[] byKind,
        RuleSettings settings,
        EmitterPlan emitterPlan,
        IndentationModel indentation,
        RuleContext context,
        SyntaxWrappingDiscovery.Result discovery,
        ClaimedBreaks claims)
    {
        _source = source;
        _stream = stream;
        _settings = settings;
        _emitterPlan = emitterPlan;
        _indentation = indentation;
        _claims = claims;
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
        // A block asked whether it stays on one line sees the breaks decided so far. A construct
        // is decided after the one holding it, so what it holds may still change; the answer is
        // not remembered until this pass is done.
        _claims.Follows(
            index => _settledBreaks.Contains(index)
                || _plannedLineBreaks.TryGetValue(index, out var placed) && placed.Contains('\n'),
            settled: false);
        // The first attribute can follow a target colon rather than the opening bracket.
        // Its horizontal gap belongs to spacing; wrapping still owns multiline placement.
        for (var index = 0; index < _boundaries.Length; index++)
        {
            var boundary = _boundaries[index];
            var left = _stream.Pieces[boundary.RightIndex - 1].Token;
            if (left.Parent is not AttributeTargetSpecifierSyntax target || left != target.ColonToken)
                continue;

            var right = _stream.Pieces[boundary.RightIndex].Token;
            var original = trivia.Trailing(boundary.RightIndex - 1).ToFullString()
                + trivia.Leading(boundary.RightIndex).ToFullString();
            var gap = trivia.HasMeaningfulGap(boundary.RightIndex)
                ? original
                : _emitterPlan.DesiredSpace(left, right) switch
                    {
                        true => " ",
                        false => "",
                        _ => trivia.HasLineBreak(boundary.RightIndex) ? " " : original
                    };
            _boundaries[index] = boundary with { SingleLineGap = gap };
        }
        if (_needsWidths)
            _plannedWidths = new(_boundaries);
        FindSettledBreaks();
        Decide();
        var gapMap = new Dictionary<int, string>(_boundaries.Length);
        var closingOpenings = new Dictionary<int, int>();
        for (var occurrenceIndex = _occurrences.Length - 1; occurrenceIndex >= 0; occurrenceIndex--)
        {
            var occurrence = _occurrences[occurrenceIndex];
            if (!occurrence.Applies)
                continue;

            var conditionDepth = MultilineConditionDepth(occurrenceIndex, gapMap);
            var existingMultilineList = IsExistingMultilineList(occurrence);
            var indentIndex = occurrence.Kind == SyntaxWrappingKind.BinaryExpressions
                ? PrecedenceGroupRoot(occurrenceIndex)
                : occurrenceIndex;
            var indent = occurrence.Multi ? Indent(indentIndex, 1 + conditionDepth) : "";
            var baseIndent = occurrence.Multi ? Indent(occurrenceIndex, 0) : "";
            var ladder = occurrence.Multi && CanUseDecisionLadder(occurrence, gapMap);
            var precedenceOperandIndent = occurrence is
                {
                    Kind: SyntaxWrappingKind.BinaryExpressions,
                    Setting: { Mode: WrappingMode.Auto or WrappingMode.Preserve, IndentationStyle: "precedence" },
                    Node: BinaryExpressionSyntax binary
                }
                && binary.Ancestors().OfType<BinaryExpressionSyntax>().None()
                        ? ExistingBinaryOperandIndent(occurrence)
                        : null;
            for (var offset = 0; offset < occurrence.BoundaryCount; offset++)
            {
                var boundary = _boundaries[occurrence.BoundaryStart + offset];
                if (PreservesBoundary(occurrence, boundary, existingMultilineList))
                    continue;
                var multi = BoundaryBreak(occurrence, offset);
                if (IsClose(boundary) && occurrence.Setting.ClosingPosition is { } position)
                {
                    if (!PlannedMultiline(occurrence, gapMap))
                        continue;
                    multi = position == "own_line";
                    baseIndent = Indent(occurrenceIndex, 0);
                    if (multi)
                    {
                        var opening = occurrence.Node is AnonymousObjectCreationExpressionSyntax anonymous
                            ? _stream.IndexOf(anonymous.OpenBraceToken, _stream.Pieces[occurrence.FirstToken].SegmentIndex)
                            : occurrence.FirstToken;
                        closingOpenings[boundary.RightIndex] = opening;
                    }
                }
                var boundaryIndent = indent;
                if (multi && occurrence is
                    { Kind: SyntaxWrappingKind.BinaryExpressions, Setting: { IndentationStyle: "precedence" } })
                {
                    var precedenceDepth = BinaryPrecedenceDepth(
                        _stream.Pieces[boundary.OperatorIndex].Token.Parent as BinaryExpressionSyntax);
                    boundaryIndent = precedenceOperandIndent is null
                        ? _indentation.Continuation(occurrence.Node, 1 + precedenceDepth, "")
                        : precedenceOperandIndent
                            + string.Concat(Enumerable.Repeat(_emitterPlan.IndentUnit, precedenceDepth));
                }
                if (occurrence is { Multi: true, Setting.NestedStyle: { } style })
                {
                    if (ladder)
                    {
                        boundaryIndent = Indent(
                            occurrenceIndex,
                            _emitterPlan.IndentBlockContents != true && _trivia!.HasLineBreak(occurrence.FirstToken) ? 0 : 1);
                        multi = boundary is { BreakWhenMulti: true, OperatorIndex: >= 0 }
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
                        boundaryIndent = Indent(occurrenceIndex, 1 + conditionDepth + depth);
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
        return new(gaps, _skippedOccurrences, closingOpenings);
    }

    /// <inheritdoc />
    public bool? StartsItsOwnLine(SyntaxNode list, SyntaxNode item)
    {
        _occurrencesByNode ??= OccurrencesByNode();
        if (!_occurrencesByNode.TryGetValue(list, out var index))
            return null;

        var occurrence = _occurrences[index];
        var first = item.GetFirstToken(includeZeroWidth: true);
        for (var offset = 0; offset < occurrence.BoundaryCount; offset++)
        {
            var boundary = _boundaries[occurrence.BoundaryStart + offset];
            if (_stream.Pieces[boundary.RightIndex].Token == first)
            {
                return _settledBoundaries.TryGetValue(boundary.RightIndex, out var breaks)
                    ? breaks
                    : null;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public bool? LinkStartsItsOwnLine(MemberAccessExpressionSyntax link)
    {
        if (_chainOperators is null)
        {
            _chainOperators = [];
            foreach (var occurrence in _occurrences)
            {
                if (occurrence.Kind != SyntaxWrappingKind.MemberAccessChains)
                    continue;
                for (var offset = 0; offset < occurrence.BoundaryCount; offset++)
                {
                    var index = _boundaries[occurrence.BoundaryStart + offset].RightIndex;
                    _chainOperators.TryAdd(_stream.Pieces[index].Token, index);
                }
            }
        }

        return _chainOperators.TryGetValue(link.OperatorToken, out var operatorIndex)
            && _settledBoundaries.TryGetValue(operatorIndex, out var breaks)
                ? breaks
                : null;
    }

    Dictionary<SyntaxNode, int> OccurrencesByNode()
    {
        var byNode = new Dictionary<SyntaxNode, int>(_occurrences.Length);
        for (var index = 0; index < _occurrences.Length; index++)
            byNode.TryAdd(_occurrences[index].Node, index);
        return byNode;
    }

    int MultilineConditionDepth(int occurrenceIndex, IReadOnlyDictionary<int, string> childGaps)
    {
        var occurrence = _occurrences[occurrenceIndex];
        if (occurrence.Node is not ConditionalExpressionSyntax conditional)
            return 0;

        var baseWidth = VisualWidth(Indent(occurrenceIndex, 0), _settings.TabWidth);
        var deepestWidth = baseWidth;
        var questionIndex = _stream.IndexOf(
            conditional.QuestionToken,
            _stream.Pieces[occurrence.FirstToken].SegmentIndex);
        for (var index = occurrence.FirstToken + 1; index < questionIndex; index++)
        {
            if (childGaps.TryGetValue(index, out var gap))
            {
                var lineStart = gap.LastIndexOfAny(['\r', '\n']) + 1;
                if (lineStart == 0)
                    continue;
                var end = lineStart;
                while (end < gap.Length && gap[end] is ' ' or '\t')
                    end++;
                deepestWidth = Math.Max(deepestWidth, VisualWidth(gap[lineStart..end], _settings.TabWidth));
            }
            else if (_trivia!.HasLineBreak(index)
                && _indentation.ExistingContinuation(_stream.Pieces[index].Token) is { } indent)
            {
                deepestWidth = Math.Max(deepestWidth, VisualWidth(indent, _settings.TabWidth));
            }
        }

        var unitWidth = Math.Max(1, VisualWidth(_emitterPlan.IndentUnit, _settings.TabWidth));
        return (deepestWidth - baseWidth + unitWidth - 1) / unitWidth;
    }

    void Decide()
    {
        for (var occurrenceIndex = 0; occurrenceIndex < _occurrences.Length; occurrenceIndex++)
        {
            var occurrence = _occurrences[occurrenceIndex];
            var initializerLayout = SyntaxWrappingRule.InitializerKindFor(occurrence.Kind) is not null;
            var itemPerLineLayout = occurrence.Kind is SyntaxWrappingKind.Arguments or SyntaxWrappingKind.Parameters or SyntaxWrappingKind.CollectionExpressions;
            var hasLineBreak = HasLineBreak(occurrence);
            var hasNestedLineBreak = (initializerLayout || occurrence.Node is BaseArgumentListSyntax { Arguments.Count: > 1 })
                && HasUnownedLineBreak(occurrence);
            if (occurrence.Setting.ShapesOperators && HasCommentedOperator(occurrence))
                continue;
            if (occurrence.Setting.Mode == WrappingMode.Preserve
                && !hasLineBreak
                && occurrence.Setting.ClosingPosition is null)
                continue;
            if (occurrence.Setting.Mode == WrappingMode.Auto
                && (hasLineBreak || hasNestedLineBreak)
                && !initializerLayout
                && !itemPerLineLayout
                && !occurrence.Setting.ShapesOperators
                && occurrence.Node is not BaseListSyntax)
                continue;

            var multi = occurrence.Setting.Mode == WrappingMode.Multi
                || occurrence.Setting is { Mode: WrappingMode.Preserve, ClosingPosition: not null }
                    && (hasLineBreak || HasUnownedLineBreak(occurrence))
                || occurrence.Setting.ShapesOperators
                    && hasLineBreak
                    && occurrence.Setting.Mode is WrappingMode.Auto or WrappingMode.Preserve
                || occurrence.Setting.Mode == WrappingMode.Auto
                    && (initializerLayout || itemPerLineLayout)
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
                if (occurrence.Node is MemberAccessExpressionSyntax or ConditionalAccessExpressionSyntax or BaseArgumentListSyntax or BaseListSyntax or BinaryExpressionSyntax
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

                    var start = VisualStartColumn(occurrence);
                    multi = (long)start + width > occurrence.Setting.MaximumLineLength;
                    if (multi && occurrence.Node is MemberAccessExpressionSyntax or ConditionalAccessExpressionSyntax)
                        multi = ChainBreakGivesRoom(occurrence, occurrenceIndex, start);
                }
            }

            if (!multi
                && occurrence.Setting.ClosingPosition is null
                && (hasLineComment || !checkedLineComments && HasLineComment(occurrence)))
            {
                if (initializerLayout && occurrence.Setting.Mode == WrappingMode.Compact)
                    multi = true;
                else
                    continue;
            }

            occurrence = occurrence with { Applies = true, Multi = multi };
            _occurrences[occurrenceIndex] = occurrence;
            var existingMultilineList = IsExistingMultilineList(occurrence);
            for (var offset = 0; offset < occurrence.BoundaryCount; offset++)
            {
                var boundary = _boundaries[occurrence.BoundaryStart + offset];
                if (PreservesBoundary(occurrence, boundary, existingMultilineList))
                    continue;
                var boundaryMulti = BoundaryBreak(occurrence, offset);
                // Where an item is about to stand is a decision, not a measurement: a list asks
                // it to indent what its items contain whether or not any width is being counted.
                _settledBoundaries[boundary.RightIndex] = boundaryMulti;
                if (_plannedWidths is null)
                    continue;
                if (boundaryMulti)
                {
                    _plannedLineBreaks[boundary.RightIndex] = Render(
                        boundary,
                        _trivia!,
                        true,
                        Indent(occurrenceIndex, 1),
                        Indent(occurrenceIndex, 0),
                        _lineEnding);
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

    /// <summary>
    /// Whether breaking a chain that does not fit on one line is worth it.
    /// </summary>
    /// <remarks>
    /// A chain too long up to its last member name needs its breaks. One that fits up to that name is pushed over
    /// only by the arguments after it, and breaking moves those arguments to a line of their own: worth it when
    /// they then fit, and otherwise only a move before they wrap anyway, which their own list does better alone.
    /// A single call is a call rather than a chain, so its arguments wrap as any call's do.
    /// </remarks>
    bool ChainBreakGivesRoom(Occurrence occurrence, int occurrenceIndex, int start)
    {
        var name = occurrence.Node.GetLastToken();
        var nameIndex = occurrence.LastToken;
        while (nameIndex > occurrence.FirstToken && _stream.Pieces[nameIndex].Token != name)
            nameIndex--;
        var limit = occurrence.Setting.MaximumLineLength;
        if (nameIndex == occurrence.LastToken || (long)start + JoinedWidth(occurrence, occurrence.FirstToken, nameIndex) > limit)
            return true;
        if (SyntaxWrappingDiscovery.ChainEnd(occurrence.Node)
                .DescendantNodesAndSelf(SyntaxWrappingDiscovery.IsChainLink)
                .OfType<InvocationExpressionSyntax>()
                .Take(2)
                .Count() < 2)
        {
            return false;
        }

        var lastBreak = -1;
        for (var offset = 0; offset < occurrence.BoundaryCount; offset++)
        {
            if (_boundaries[occurrence.BoundaryStart + offset].BreakWhenMulti)
                lastBreak = offset;
        }

        return lastBreak >= 0
            && (long)VisualWidth(Indent(occurrenceIndex, 1), _settings.TabWidth)
                    + JoinedWidth(occurrence, _boundaries[occurrence.BoundaryStart + lastBreak].RightIndex, occurrence.LastToken)
                <= limit;
    }

    /// <summary>The width of a run of tokens on one line, with this occurrence's own boundaries joined.</summary>
    int JoinedWidth(Occurrence occurrence, int from, int to)
    {
        var width = 0;
        var offset = 0;
        for (var index = from; index <= to; index++)
        {
            if (index != from)
            {
                while (offset < occurrence.BoundaryCount && _boundaries[occurrence.BoundaryStart + offset].RightIndex < index)
                    offset++;
                width += offset < occurrence.BoundaryCount
                    && _boundaries[occurrence.BoundaryStart + offset] is { } boundary
                    && boundary.RightIndex == index
                        ? PlannedWidth(boundary, _trivia!, false, _lineEnding, _settings.TabWidth)
                        : CurrentGapWidth(index);
            }

            width += VisualWidth(_stream.Pieces[index].Token.Text, _settings.TabWidth);
        }

        return width;
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
        if (IsClose(boundary) && occurrence.Setting.ClosingPosition is { } position)
            return occurrence.Multi && position == "own_line";
        if (!boundary.BreakWhenMulti)
            return false;
        if (occurrence.Setting is { Mode: WrappingMode.Preserve, NestedStyle: null } && boundary.OperatorIndex >= 0)
            return _trivia!.HasLineBreak(boundary.OperatorIndex) || _trivia.HasLineBreak(boundary.OperatorIndex + 1);
        return _chainBoundaryBreaks.GetValueOrDefault(occurrence.BoundaryStart + offset, occurrence.Multi);
    }

    static bool IsClose(Boundary boundary) =>
        boundary.Style is GapStyle.DelimitedClose or GapStyle.DelimitedSpacedClose;

    bool PreservesBoundary(Occurrence occurrence, Boundary boundary, bool existingMultilineList) =>
        IsClose(boundary) && occurrence.Setting.ClosingPosition is not null && _trivia!.HasMeaningfulGap(boundary.RightIndex)
        || occurrence.Setting is { Mode: WrappingMode.Preserve, ClosingPosition: not null } && !IsClose(boundary)
        || existingMultilineList
            && !(IsClose(boundary) && occurrence.Setting.ClosingPosition is not null)
            && (boundary.Style == GapStyle.DelimitedClose
            || _emitterPlan.IndentBlockContents is null && _trivia!.HasLineBreak(boundary.RightIndex));

    bool PlannedMultiline(Occurrence occurrence, IReadOnlyDictionary<int, string> gaps)
    {
        if (occurrence.Multi)
            return true;
        if (occurrence.Setting.Mode is WrappingMode.Preserve or WrappingMode.Auto
            && _trivia!.HasLineBreak(occurrence.LastToken))
        {
            return true;
        }
        for (var index = occurrence.FirstToken; index <= occurrence.LastToken; index++)
        {
            if (_stream.Pieces[index].Token.Text.Contains('\n'))
                return true;
            if (index == occurrence.FirstToken)
                continue;
            if (gaps.TryGetValue(index, out var gap) ? gap.Contains('\n')
                : _settledBoundaries.TryGetValue(index, out var breaks) ? breaks
                : BreaksAt(index))
            {
                return true;
            }
        }
        return false;
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

    string? ExistingBinaryOperandIndent(Occurrence occurrence) =>
        _trivia!.HasLineBreak(occurrence.FirstToken)
            ? _indentation.ExistingContinuation(_stream.Pieces[occurrence.FirstToken].Token)
            : null;

    bool CanUseDecisionLadder(Occurrence occurrence, IReadOnlyDictionary<int, string> childGaps)
    {
        if (occurrence.Setting.NestedStyle != "decision_ladder"
            || occurrence.Node is not ConditionalExpressionSyntax { WhenFalse: ConditionalExpressionSyntax } root)
        {
            return false;
        }

        // Only the false-branch spine can become a ladder. A nested true branch remains a tree.
        for (var expression = root;; )
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
            if (index > occurrence.FirstToken
                && !owned.Contains(index)
                && (childGaps.TryGetValue(index, out var gap)
                    ? gap.Contains('\n') || gap.Contains('\r')
                    : _trivia!.HasLineBreak(index)))
            {
                return false;
            }
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
                {
                    Advance(gap);
                }
                else if (_plannedWidths!.TryGet(index, out var width))
                {
                    column += width;
                }
                else if (_trivia!.HasLineBreak(index) || _trivia.HasMeaningfulGap(index))
                {
                    foreach (var item in _trivia.Trailing(index - 1))
                        Advance(item.ToFullString());
                    foreach (var item in _trivia.Leading(index))
                        Advance(item.ToFullString());
                }
                else
                {
                    column += BaseGapWidth(index);
                }
            }

            if (offset < boundaryLines.Length
                && _boundaries[occurrence.BoundaryStart + offset].RightIndex == index)
            {
                boundaryLines[offset++] = lineWidths.Count;
            }
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
                {
                    column += character == '\t' ? _settings.TabWidth - column % _settings.TabWidth : 1;
                }
            }
        }
    }

    bool IsExistingMultilineList(Occurrence occurrence) =>
        occurrence.Setting.Mode == WrappingMode.Auto
        && occurrence.Kind is SyntaxWrappingKind.Arguments or SyntaxWrappingKind.Parameters or SyntaxWrappingKind.CollectionExpressions
        && HasLineBreak(occurrence);

    bool HasLineBreak(Occurrence occurrence)
    {
        for (var offset = 0; offset < occurrence.BoundaryCount; offset++)
        {
            var boundary = _boundaries[occurrence.BoundaryStart + offset];
            // A closing-position preference can add a line without expanding the items.
            // That line must not become an item-layout trigger on the next run.
            if (IsClose(boundary) && occurrence.Setting.ClosingPosition is not null)
                continue;
            if ((boundary.RightIndex != occurrence.FirstToken || boundary.BreakWhenMulti)
                && BreaksAt(boundary.RightIndex))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the file will hold a line break at this gap.
    /// </summary>
    /// <remarks>
    /// Not every break in the finished file was in the source: a brace rule can put one before a
    /// block, a query clause rule before each clause. A construct is laid out around what will be
    /// inside it, so the breaks those rules are about to claim count as much as the ones already
    /// there.
    /// </remarks>
    bool BreaksAt(int index) =>
        _trivia!.HasLineBreak(index)
        || _settledBreaks.Contains(index)
        || _claims.Before(
            _stream.Pieces[index - 1].Token,
            _stream.Pieces[index].Token,
            leftOpenedItsLine: false) == true;

    /// <summary>
    /// Finds the boundaries the settings break whatever anything measures.
    /// </summary>
    /// <remarks>
    /// A construct configured to be laid out one item per line spans lines before a column has
    /// been counted, so the construct holding it can be laid out around that without waiting for
    /// it to be decided — which it is not, because a construct is decided after the one holding it.
    /// </remarks>
    void FindSettledBreaks()
    {
        foreach (var occurrence in _occurrences)
        {
            if (occurrence.Setting.Mode != WrappingMode.Multi
                || occurrence.Setting.ShapesOperators && HasCommentedOperator(occurrence))
            {
                continue;
            }

            for (var offset = 0; offset < occurrence.BoundaryCount; offset++)
            {
                var boundary = _boundaries[occurrence.BoundaryStart + offset];
                if (boundary.BreakWhenMulti
                    && !(IsClose(boundary) && occurrence.Setting.ClosingPosition == "after_last_item"))
                {
                    _settledBreaks.Add(boundary.RightIndex);
                }
            }
        }

        // A multiline token can require an own-line close without wrapping any item.
        // Publish those closes from the inside out before enclosing lists decide their layout.
        for (var occurrenceIndex = _occurrences.Length - 1; occurrenceIndex >= 0; occurrenceIndex--)
        {
            var occurrence = _occurrences[occurrenceIndex];
            if (occurrence.Setting.ClosingPosition != "own_line"
                || occurrence.Setting.Mode is WrappingMode.Single or WrappingMode.Compact)
                continue;
            for (var index = occurrence.FirstToken + 1; index < occurrence.LastToken; index++)
            {
                if (!BreaksAt(index) && !_stream.Pieces[index].Token.Text.Contains('\n'))
                    continue;
                _settledBreaks.Add(occurrence.LastToken);
                break;
            }
        }
    }

    bool HasUnownedLineBreak(Occurrence occurrence)
    {
        // Delimited lists contribute boundaries in token order. Advance once through them rather
        // than searching the whole list for every line break in a large initializer or call.
        var boundaryIndex = occurrence.BoundaryStart;
        var boundaryEnd = boundaryIndex + occurrence.BoundaryCount;
        // The break before the first token placed the construct; it is not inside it. Counting it
        // would expand a list only because the line it landed on was given to it.
        for (var index = occurrence.FirstToken + 1; index <= occurrence.LastToken; index++)
        {
            if (!BreaksAt(index))
                continue;

            while (boundaryIndex < boundaryEnd && _boundaries[boundaryIndex].RightIndex < index)
                boundaryIndex++;

            if (boundaryIndex == boundaryEnd || _boundaries[boundaryIndex].RightIndex != index)
                return true;
        }

        return false;
    }

    int VisualStartColumn(Occurrence occurrence)
    {
        // The first break of an initializer or base list is the one being decided, so its start
        // column is where the opening token lands when joined to the previous token, not where the
        // source happens to hold it. Measuring from the source line would join a colon that then
        // fails to fit and wrap it again on the next run.
        if (occurrence is { Node: InitializerExpressionSyntax or BaseListSyntax, FirstToken: > 0 })
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

        if (PlannedBreakIndent(first) is { } placed)
            column = placed;
        else if (PlannedLineIndent(first) is { } planned)
            column = VisualWidth(planned, _settings.TabWidth);

        for (var index = first; index < firstToken; index++)
        {
            column = AdvanceColumn(pieces[index].Token.Text, column, _settings.TabWidth);
            column = AdvancePrefixGap(index + 1, column);
        }

        return column;
    }

    /// <summary>
    /// The column a break this pass has already decided leaves the token after it in, or null
    /// when no decided break puts that token at the start of a line.
    /// </summary>
    /// <remarks>
    /// A token later in the line has its gap walked by the loop that measures up to the list, so
    /// a break there already moves the column. The gap before the line's own first token is the
    /// one nothing else reads, and it is the one that decides where the whole line begins.
    /// </remarks>
    int? PlannedBreakIndent(int first)
    {
        if (_plannedLineBreaks.Count == 0
            || !_plannedLineBreaks.TryGetValue(first, out var placement)
            || !placement.Contains('\n'))
        {
            return null;
        }

        return AdvanceColumn(placement, 0, _settings.TabWidth);
    }

    /// <summary>
    /// The indentation this file is about to be written with at the token that opens a line, or
    /// null when nothing but the source says where that line starts.
    /// </summary>
    /// <remarks>
    /// Measuring a line against the column its author left it in measures a line this pass is
    /// about to move, and a length decided that way has to be taken again once the file is written.
    /// A token that opens a piece of content its owner lays out already knows where it is going.
    /// With no indentation preference the emitter leaves every line where its author put it, and
    /// the source column is the one the line will be written at.
    /// </remarks>
    string? PlannedLineIndent(int index)
    {
        if (_emitterPlan.IndentBlockContents is null)
            return null;
        if (_lineIndents.TryGetValue(index, out var cached))
            return cached;

        var token = _stream.Pieces[index].Token;
        // A node starts where its first token with width does, so asking the node is a field read
        // rather than a walk down its left spine.
        var indent = _indentation.DirectContentFor(token) is { } content
            && content.SpanStart == token.SpanStart
                ? _indentation.ForNode(content)
                : null;
        _lineIndents[index] = indent;
        return indent;
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
        return source.AsSpan(0, position).LastIndexOfAny('\r', '\n') + 1;
    }

    string Indent(int occurrenceIndex, int extra) =>
        PlannedBase(occurrenceIndex) + string.Concat(Enumerable.Repeat(_emitterPlan.IndentUnit, extra));

    /// <summary>
    /// The indent of the line this occurrence starts on, as this plan lays it out.
    /// </summary>
    /// <remarks>
    /// A construct indents what it holds one level in from the line it starts on, so the only thing
    /// its contents need to know is where that line begins. Asking the source instead answers about
    /// a layout this plan is replacing, and asking the syntax answers about a statement that may be
    /// several breaks away; asking the break that placed this occurrence answers about the line it
    /// will actually sit on, which is the question. Nothing is enclosed by a break that comes after
    /// it, so the walk terminates at the outermost construct that owns its own line.
    /// </remarks>
    string PlannedBase(int occurrenceIndex)
    {
        if (_plannedBases.TryGetValue(occurrenceIndex, out var planned))
            return planned;

        var structural = StructuralBase(occurrenceIndex);
        _plannedBases[occurrenceIndex] = structural;
        var placed = PlacingOccurrence(occurrenceIndex);
        if (placed < 0)
        {
            // Sharing a line with the construct that holds it would give a parenthesized group the
            // same column as that construct's own continuations, so the group opens a level of its
            // own. A break already gave it a line of its own, and then it needs no help.
            return _plannedBases[occurrenceIndex] = structural
                + string.Concat(Enumerable.Repeat(
                    _emitterPlan.IndentUnit,
                    EnclosingParentheses(_occurrences[occurrenceIndex].Node)));
        }

        // The syntax already accounts for a break it declares, such as a brace that opens a block
        // or a setting that indents one; the plan only knows the breaks it decided itself.
        var placedIndent = Indent(placed, 1);
        return _plannedBases[occurrenceIndex] =
            VisualWidth(placedIndent, _settings.TabWidth) > VisualWidth(structural, _settings.TabWidth)
            ? placedIndent
            : structural;
    }

    /// <summary>
    /// How many parenthesized groups hold <paramref name="node"/> within its own statement.
    /// </summary>
    static int EnclosingParentheses(SyntaxNode node)
    {
        var depth = 0;
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is ParenthesizedExpressionSyntax)
                depth++;
            if (parent is StatementSyntax or MemberDeclarationSyntax or AnonymousFunctionExpressionSyntax)
                break;
        }

        return depth;
    }

    /// <summary>
    /// The enclosing occurrence whose break puts <paramref name="occurrenceIndex"/> on its line.
    /// </summary>
    int PlacingOccurrence(int occurrenceIndex)
    {
        var occurrence = _occurrences[occurrenceIndex];
        var placed = -1;
        var placedAt = -1;
        for (var parentIndex = occurrence.Parent; parentIndex >= 0; )
        {
            var parent = _occurrences[parentIndex];
            for (var offset = 0; offset < parent.BoundaryCount; offset++)
            {
                var boundary = _boundaries[parent.BoundaryStart + offset];
                if (boundary.RightIndex > occurrence.FirstToken)
                    break;
                if (boundary.RightIndex > placedAt
                    && (BoundaryBreak(parent, offset) || _trivia!.HasLineBreak(boundary.RightIndex)))
                {
                    placedAt = boundary.RightIndex;
                    placed = parentIndex;
                }
            }
            parentIndex = parent.Parent;
        }

        return placed;
    }

    /// <summary>
    /// Where an occurrence no break reaches sits, from the syntax that owns it.
    /// </summary>
    string StructuralBase(int occurrenceIndex)
    {
        var occurrence = _occurrences[occurrenceIndex];
        var node = occurrence.Node;
        if (_emitterPlan.IndentBlockContents is not null)
            return _indentation.Continuation(node, 0, "");

        var pieceIndex = node is InitializerExpressionSyntax or CollectionExpressionSyntax or BaseListSyntax
            ? Math.Max(0, occurrence.FirstToken - 1)
            : occurrence.FirstToken;
        var piece = _stream.Pieces[pieceIndex];
        var text = Microsoft.CodeAnalysis.Text.SourceText.From(piece.Source);
        var line = text.Lines.GetLineFromPosition(piece.Token.SpanStart);
        var leading = 0;
        while (line.Start + leading < line.End && char.IsWhiteSpace(text[line.Start + leading]))
            leading++;

        return _indentation.Continuation(node, 0, piece.Source.Substring(line.Start, leading));
    }

    /// <summary>
    /// How many parenthesized groups stand between <paramref name="node"/> and
    /// <paramref name="ancestor"/>.
    /// </summary>
    /// <remarks>
    /// An indentation style says how far a step in precedence goes, and "flat" says nowhere.
    /// Parentheses are not a step in precedence: they open a construct, and a construct indents
    /// what it holds whatever the style says about precedence.
    /// </remarks>
    static int ParenthesisDepth(SyntaxNode? node, SyntaxNode ancestor)
    {
        var depth = 0;
        for (var parent = node; parent is not null && parent != ancestor; parent = parent.Parent)
        {
            if (parent is ParenthesizedExpressionSyntax)
                depth++;
        }

        return depth;
    }

    /// <summary>
    /// The occurrence whose line a binary expression's continuations indent from.
    /// </summary>
    /// <remarks>
    /// A step in precedence splits one expression into an occurrence per operator, each starting on
    /// the line the step before it broke to. Indenting from that line would add a level per step,
    /// which is what an indentation style of "flat" says not to do, so every step in one group
    /// answers from where the group itself began. Parentheses end a group: they open a construct.
    /// </remarks>
    int PrecedenceGroupRoot(int occurrenceIndex)
    {
        while (_occurrences[occurrenceIndex] is { Parent: >= 0 } occurrence
            && _occurrences[occurrence.Parent] is { Kind: SyntaxWrappingKind.BinaryExpressions } parent
            && ParenthesisDepth(occurrence.Node, parent.Node) == 0)
        {
            occurrenceIndex = occurrence.Parent;
        }

        return occurrenceIndex;
    }

    static int BinaryPrecedenceDepth(BinaryExpressionSyntax? binary)
    {
        var depth = 0;
        if (binary is null)
            return depth;

        var precedence = BinaryPrecedence(binary.Kind());
        foreach (var parentBinary in binary.Ancestors().OfType<BinaryExpressionSyntax>())
        {
            var parentPrecedence = BinaryPrecedence(parentBinary.Kind());
            if (precedence > parentPrecedence)
                depth++;
            precedence = parentPrecedence;
        }

        return depth;
    }

    static int BinaryPrecedence(SyntaxKind kind) => kind switch
        {
            SyntaxKind.MultiplyExpression or SyntaxKind.DivideExpression or SyntaxKind.ModuloExpression => 11,
            SyntaxKind.AddExpression or SyntaxKind.SubtractExpression => 10,
            SyntaxKind.LeftShiftExpression or SyntaxKind.RightShiftExpression or SyntaxKind.UnsignedRightShiftExpression => 9,
            SyntaxKind.LessThanExpression or SyntaxKind.LessThanOrEqualExpression
                or SyntaxKind.GreaterThanExpression or SyntaxKind.GreaterThanOrEqualExpression
                or SyntaxKind.IsExpression or SyntaxKind.AsExpression => 8,
            SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression => 7,
            SyntaxKind.BitwiseAndExpression => 6,
            SyntaxKind.ExclusiveOrExpression => 5,
            SyntaxKind.BitwiseOrExpression => 4,
            SyntaxKind.LogicalAndExpression => 3,
            SyntaxKind.LogicalOrExpression => 2,
            SyntaxKind.CoalesceExpression => 1,
            _ => 0
        };

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
