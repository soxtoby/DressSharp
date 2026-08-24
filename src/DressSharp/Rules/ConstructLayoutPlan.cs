using DressSharp.Architecture;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

/// <summary>
/// The whitespace construct layout owns, prepared without rebuilding the syntax tree.
/// </summary>
sealed class ConstructLayoutPlan
{
    readonly PlannedGap[] _gaps;
    int _nextGap;

    ConstructLayoutPlan(PlannedGap[] gaps, int skippedOccurrences)
    {
        _gaps = gaps;
        SkippedOccurrences = skippedOccurrences;
    }

    internal int SkippedOccurrences { get; }

    internal static Preparation Prepare(
        SyntaxNode root,
        string source,
        SyntaxRewritePlan rewrites,
        IReadOnlyList<ConstructLayoutRule> rules,
        FormattingConfiguration configuration,
        RuleSettings settings,
        EmitterPlan emitterPlan,
        RuleContext context)
    {
        var byKind = new ConstructLayoutRule.Setting?[(int)ConstructLayoutKind.Attributes + 1];
        var hasEnabledRule = false;
        foreach (var rule in rules)
        {
            if (!configuration.Preferences.TryGetValue(rule.Metadata.RuleKey, out var preference)
                || preference.Equals("unset", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!rule.Metadata.AcceptedValueForms.Contains(preference, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Invalid value '{preference}' for '{rule.Metadata.RuleKey.ToName()}'.");
            }

            byKind[(int)rule.Kind] = ConstructLayoutRule.Setting.For(preference, settings.MaximumLineLength);
            hasEnabledRule = true;
        }

        if (!hasEnabledRule)
            return new(EffectiveTokenStream.For(root, source, rewrites), null);

        var stream = EffectiveTokenStream.ForConstructs(
            root,
            source,
            rewrites,
            ConstructTriggerMask.For(byKind));
        var planner = new Planner(root, source, stream, byKind, settings, emitterPlan, context);
        planner.Discover();
        return new(stream, planner);
    }

    internal sealed class Preparation(EffectiveTokenStream stream, Planner? planner)
    {
        internal EffectiveTokenStream Stream { get; } = stream;

        internal ConstructLayoutPlan Finish(TriviaLayoutPlan trivia) =>
            planner?.Finish(trivia) ?? new([], 0);
    }

    internal bool TryGetGap(int index, out string gap)
    {
        while (_nextGap < _gaps.Length && _gaps[_nextGap].Index < index)
            _nextGap++;
        if (_nextGap >= _gaps.Length || _gaps[_nextGap].Index != index)
        {
            gap = null!;
            return false;
        }

        gap = _gaps[_nextGap].Text;
        return true;
    }

    readonly record struct PlannedGap(int Index, string Text);

    static bool ContainsLineComment(SyntaxTriviaList trivia)
    {
        return trivia.Any(item =>
            item.IsKind(SyntaxKind.SingleLineCommentTrivia)
            || item.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia));
    }

    internal sealed class Planner
    {
        readonly SyntaxNode _root;
        readonly string _source;
        readonly EffectiveTokenStream _stream;
        readonly ConstructLayoutRule.Setting?[] _byKind;
        readonly RuleSettings _settings;
        readonly EmitterPlan _emitterPlan;
        readonly RuleContext? _safetyContext;
        readonly string _lineEnding;
        readonly bool _needsWidths;
        readonly List<Occurrence> _occurrences = [];
        readonly List<Boundary> _boundaries = [];

        TriviaLayoutPlan? _trivia;
        SparseGapWidths? _plannedWidths;
        Dictionary<int, int>? _baseGapWidths;
        int _currentSegmentIndex;

        internal Planner(
            SyntaxNode root,
            string source,
            EffectiveTokenStream stream,
            ConstructLayoutRule.Setting?[] byKind,
            RuleSettings settings,
            EmitterPlan emitterPlan,
            RuleContext context)
        {
            _root = root;
            _source = source;
            _stream = stream;
            _byKind = byKind;
            _settings = settings;
            _emitterPlan = emitterPlan;
            _lineEnding = context.LineEnding;
            if (root.ContainsDiagnostics || root.ContainsDirectives)
                _safetyContext = new(root, settings);

            foreach (var setting in byKind)
            {
                _needsWidths |= setting is
                        { Preference: ConstructLayoutPreference.Auto, Maximum: not int.MaxValue };
            }
        }

        internal void Discover()
        {
            foreach (var index in _stream.TriggerIndices)
            {
                _currentSegmentIndex = _stream.Pieces[index].SegmentIndex;
                Discover(_stream.Pieces[index].Token);
            }

            SortAndLinkOccurrences();
        }

        internal ConstructLayoutPlan Finish(TriviaLayoutPlan trivia)
        {
            _trivia = trivia;
            if (_needsWidths)
                _plannedWidths = new(_boundaries);
            Decide();
            var gapMap = new Dictionary<int, string>(_boundaries.Count);
            for (var occurrenceIndex = _occurrences.Count - 1; occurrenceIndex >= 0; occurrenceIndex--)
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

            var gaps = new PlannedGap[gapMap.Count];
            var index = 0;
            foreach (var (tokenIndex, text) in gapMap)
                gaps[index++] = new(tokenIndex, text);
            Array.Sort(gaps, static (left, right) => left.Index.CompareTo(right.Index));
            return new(gaps, _safetyContext?.TakeSkippedOccurrences() ?? 0);
        }

        void Discover(SyntaxToken token)
        {
            switch ((SyntaxKind)token.RawKind)
            {
                case SyntaxKind.OpenParenToken:
                    if (Enabled(ConstructLayoutKind.Arguments) is not null
                        && token.Parent is ArgumentListSyntax { Arguments.Count: > 0 } arguments
                        && token == arguments.OpenParenToken)
                    {
                        AddOccurrence(arguments, ConstructLayoutKind.Arguments);
                    }
                    else if (Enabled(ConstructLayoutKind.Parameters) is not null
                        && token.Parent is ParameterListSyntax { Parameters.Count: > 0 } parameters
                        && token == parameters.OpenParenToken)
                    {
                        AddOccurrence(parameters, ConstructLayoutKind.Parameters);
                    }

                    break;
                case SyntaxKind.OpenBracketToken:
                    DiscoverOpenBracket(token);
                    break;
                case SyntaxKind.OpenBraceToken:
                    if (Enabled(ConstructLayoutKind.Initializers) is not null
                        && token.Parent is InitializerExpressionSyntax { Expressions.Count: > 0 } initializer
                        && token == initializer.OpenBraceToken)
                    {
                        AddOccurrence(initializer, ConstructLayoutKind.Initializers);
                    }

                    break;
                case SyntaxKind.ColonToken:
                    if (Enabled(ConstructLayoutKind.BaseTypeLists) is not null
                        && token.Parent is BaseListSyntax { Types.Count: > 0 } baseList
                        && token == baseList.ColonToken)
                    {
                        AddOccurrence(baseList, ConstructLayoutKind.BaseTypeLists);
                    }

                    break;
                case SyntaxKind.WhereKeyword:
                    DiscoverConstraints(token);
                    break;
                case SyntaxKind.FromKeyword:
                    DiscoverQuery(token);
                    break;
                case SyntaxKind.DotToken:
                case SyntaxKind.MinusGreaterThanToken:
                    DiscoverMemberAccess(token);
                    break;
                case SyntaxKind.QuestionToken:
                    DiscoverQuestion(token);
                    break;
                default:
                    DiscoverBinary(token);
                    break;
            }
        }

        void DiscoverOpenBracket(SyntaxToken token)
        {
            if (Enabled(ConstructLayoutKind.Arguments) is not null
                && token.Parent is BracketedArgumentListSyntax { Arguments.Count: > 0 } arguments
                && token == arguments.OpenBracketToken)
            {
                AddOccurrence(arguments, ConstructLayoutKind.Arguments);
            }
            else if (Enabled(ConstructLayoutKind.Parameters) is not null
                && token.Parent is BracketedParameterListSyntax { Parameters.Count: > 0 } parameters
                && token == parameters.OpenBracketToken)
            {
                AddOccurrence(parameters, ConstructLayoutKind.Parameters);
            }
            else if (Enabled(ConstructLayoutKind.CollectionExpressions) is not null
                && token.Parent is CollectionExpressionSyntax { Elements.Count: > 0 } collection
                && token == collection.OpenBracketToken)
            {
                AddOccurrence(collection, ConstructLayoutKind.CollectionExpressions);
            }
            else if (Enabled(ConstructLayoutKind.Attributes) is not null
                && token.Parent is AttributeListSyntax { Attributes.Count: > 0 } attributes
                && token == attributes.OpenBracketToken)
            {
                AddOccurrence(attributes, ConstructLayoutKind.Attributes);
            }
        }

        void DiscoverConstraints(SyntaxToken token)
        {
            if (Enabled(ConstructLayoutKind.ConstraintClauses) is null
                || token.Parent is not TypeParameterConstraintClauseSyntax clause
                || token != clause.WhereKeyword
                || clause.Parent is not SyntaxNode declaration
                || !IsFirstConstraint(declaration, clause))
            {
                return;
            }

            AddOccurrence(declaration, ConstructLayoutKind.ConstraintClauses);
        }

        static bool IsFirstConstraint(SyntaxNode declaration, TypeParameterConstraintClauseSyntax clause) =>
            declaration switch
                {
                    TypeDeclarationSyntax { ConstraintClauses.Count: > 0 } item =>
                        item.ConstraintClauses[0] == clause,
                    MethodDeclarationSyntax { ConstraintClauses.Count: > 0 } item =>
                        item.ConstraintClauses[0] == clause,
                    LocalFunctionStatementSyntax { ConstraintClauses.Count: > 0 } item =>
                        item.ConstraintClauses[0] == clause,
                    DelegateDeclarationSyntax { ConstraintClauses.Count: > 0 } item =>
                        item.ConstraintClauses[0] == clause,
                    _ => false
                };

        void DiscoverQuery(SyntaxToken token)
        {
            if (Enabled(ConstructLayoutKind.QueryClauses) is null
                || token.Parent is not FromClauseSyntax from
                || token != from.FromKeyword)
            {
                return;
            }

            for (var parent = from.Parent; parent is not null; parent = parent.Parent)
            {
                if (parent is QueryExpressionSyntax query)
                {
                    if (query.FromClause == from)
                        AddOccurrence(query, ConstructLayoutKind.QueryClauses);
                    return;
                }

                if (parent is not (QueryClauseSyntax or QueryContinuationSyntax))
                    return;
            }
        }

        void DiscoverMemberAccess(SyntaxToken token)
        {
            if (Enabled(ConstructLayoutKind.MemberAccessChains) is not null
                && token.Parent is MemberAccessExpressionSyntax access
                && token == access.OperatorToken
                && access.Parent is not MemberAccessExpressionSyntax)
            {
                AddOccurrence(access, ConstructLayoutKind.MemberAccessChains);
            }
        }

        void DiscoverQuestion(SyntaxToken token)
        {
            if (Enabled(ConstructLayoutKind.MemberAccessChains) is not null
                && token.Parent is ConditionalAccessExpressionSyntax access
                && token == access.OperatorToken
                && access.Parent is not MemberAccessExpressionSyntax
                && access.Parent is not ConditionalAccessExpressionSyntax)
            {
                AddOccurrence(access, ConstructLayoutKind.MemberAccessChains);
            }

            if (Enabled(ConstructLayoutKind.ConditionalExpressions) is not null
                && token.Parent is ConditionalExpressionSyntax conditional
                && token == conditional.QuestionToken
                && conditional.Parent is not ConditionalExpressionSyntax)
            {
                AddOccurrence(conditional, ConstructLayoutKind.ConditionalExpressions);
            }
        }

        void DiscoverBinary(SyntaxToken token)
        {
            if (Enabled(ConstructLayoutKind.BinaryExpressions) is null
                || !ConstructTriggerMask.IsBinaryOperator((SyntaxKind)token.RawKind)
                || token.Parent is not BinaryExpressionSyntax binary
                || token != binary.OperatorToken
                || binary.Parent is BinaryExpressionSyntax)
            {
                return;
            }

            AddOccurrence(binary, ConstructLayoutKind.BinaryExpressions);
        }

        void AddOccurrence(SyntaxNode node, ConstructLayoutKind kind)
        {
            if (Enabled(kind) is not { } setting || Unsafe(node))
                return;

            var start = _boundaries.Count;
            var firstToken = _stream.IndexOf(node.GetFirstToken(), _currentSegmentIndex);
            var lastToken = _stream.IndexOf(OccurrenceLastToken(node), _currentSegmentIndex);

            switch (node)
            {
                case ArgumentListSyntax list:
                    Delimited(list.Arguments, list.CloseParenToken);
                    break;
                case BracketedArgumentListSyntax list:
                    Delimited(list.Arguments, list.CloseBracketToken);
                    break;
                case ParameterListSyntax list:
                    Delimited(list.Parameters, list.CloseParenToken);
                    break;
                case BracketedParameterListSyntax list:
                    Delimited(list.Parameters, list.CloseBracketToken);
                    break;
                case InitializerExpressionSyntax initializer:
                    Delimited(initializer.Expressions, initializer.CloseBraceToken);
                    break;
                case CollectionExpressionSyntax collection:
                    Delimited(collection.Elements, collection.CloseBracketToken);
                    break;
                case AttributeListSyntax attributes:
                    Delimited(attributes.Attributes, attributes.CloseBracketToken);
                    break;
                case BaseListSyntax baseList:
                    for (var index = 0; index < baseList.Types.Count; index++)
                    {
                        var right = baseList.Types[index].GetFirstToken();
                        AddBoundary(
                            right,
                            index == 0 || !right.GetPreviousToken().IsKind(SyntaxKind.CommaToken)
                                ? GapStyle.SeparatedFirst
                                : GapStyle.SeparatedLater);
                    }

                    break;
                case TypeDeclarationSyntax declaration:
                    Constraints(declaration.ConstraintClauses);
                    break;
                case MethodDeclarationSyntax declaration:
                    Constraints(declaration.ConstraintClauses);
                    break;
                case LocalFunctionStatementSyntax declaration:
                    Constraints(declaration.ConstraintClauses);
                    break;
                case DelegateDeclarationSyntax declaration:
                    Constraints(declaration.ConstraintClauses);
                    break;
                case MemberAccessExpressionSyntax:
                    MemberAccess(firstToken, lastToken);
                    break;
                case ConditionalAccessExpressionSyntax:
                    MemberAccess(firstToken, lastToken);
                    break;
                case BinaryExpressionSyntax:
                    Binary(firstToken, lastToken);
                    break;
                case ConditionalExpressionSyntax:
                    Conditional(firstToken, lastToken);
                    break;
                case QueryExpressionSyntax query:
                    QueryBody(query.Body);
                    break;
            }

            _occurrences.Add(new(
                node,
                setting,
                start,
                _boundaries.Count - start,
                -1,
                firstToken,
                lastToken));
        }

        void QueryBody(QueryBodySyntax body)
        {
            while (true)
            {
                foreach (var clause in body.Clauses)
                    AddBoundary(clause.GetFirstToken(), GapStyle.Item);
                AddBoundary(body.SelectOrGroup.GetFirstToken(), GapStyle.Item);
                if (body.Continuation is not { } continuation)
                    return;
                AddBoundary(continuation.IntoKeyword, GapStyle.Item);
                body = continuation.Body;
            }
        }

        void MemberAccess(int firstToken, int lastToken)
        {
            var pieces = _stream.Pieces;
            for (var index = firstToken; index <= lastToken; index++)
            {
                var token = pieces[index].Token;
                if (token.IsKind(SyntaxKind.QuestionToken)
                    && token.Parent is ConditionalAccessExpressionSyntax
                    || (token.IsKind(SyntaxKind.DotToken)
                        && !token.GetPreviousToken().IsKind(SyntaxKind.QuestionToken))
                    || token.IsKind(SyntaxKind.MinusGreaterThanToken))
                {
                    AddBoundary(index, GapStyle.CompactItem);
                }
            }
        }

        void Binary(int firstToken, int lastToken)
        {
            var pieces = _stream.Pieces;
            for (var index = firstToken; index <= lastToken; index++)
            {
                var token = pieces[index].Token;
                if (token.Parent is BinaryExpressionSyntax expression
                    && token == expression.OperatorToken)
                {
                    AddBoundary(index, GapStyle.Item);
                }
            }
        }

        void Conditional(int firstToken, int lastToken)
        {
            var pieces = _stream.Pieces;
            for (var index = firstToken; index <= lastToken; index++)
            {
                var token = pieces[index].Token;
                if (token.Parent is ConditionalExpressionSyntax expression
                    && (token == expression.QuestionToken || token == expression.ColonToken))
                {
                    AddBoundary(index, GapStyle.Item);
                }
            }
        }

        static SyntaxToken OccurrenceLastToken(SyntaxNode node) => node switch
            {
                TypeDeclarationSyntax { ConstraintClauses.Count: > 0 } declaration =>
                    declaration.ConstraintClauses[^1].GetLastToken(),
                MethodDeclarationSyntax { ConstraintClauses.Count: > 0 } declaration =>
                    declaration.ConstraintClauses[^1].GetLastToken(),
                LocalFunctionStatementSyntax { ConstraintClauses.Count: > 0 } declaration =>
                    declaration.ConstraintClauses[^1].GetLastToken(),
                DelegateDeclarationSyntax { ConstraintClauses.Count: > 0 } declaration =>
                    declaration.ConstraintClauses[^1].GetLastToken(),
                _ => node.GetLastToken()
            };

        void Delimited<T>(SeparatedSyntaxList<T> items, SyntaxToken close) where T : SyntaxNode
        {
            for (var index = 0; index < items.Count; index++)
                AddBoundary(items[index].GetFirstToken(), index == 0 ? GapStyle.DelimitedFirst : GapStyle.DelimitedLater);
            AddBoundary(close, GapStyle.DelimitedClose);
        }

        void Constraints<T>(SyntaxList<T> clauses) where T : SyntaxNode
        {
            foreach (var clause in clauses)
                AddBoundary(clause.GetFirstToken(), GapStyle.Item);
        }

        void AddBoundary(SyntaxToken right, GapStyle style) =>
            _boundaries.Add(new(_stream.IndexOf(right, _currentSegmentIndex), style));

        void AddBoundary(int rightIndex, GapStyle style) =>
            _boundaries.Add(new(rightIndex, style));

        void SortAndLinkOccurrences()
        {
            if (_occurrences.Count < 2)
                return;

            _occurrences.Sort(static (left, right) =>
                {
                    var first = left.FirstToken.CompareTo(right.FirstToken);
                    return first != 0
                        ? first
                        : right.LastToken.CompareTo(left.LastToken);
                });

            var ancestors = new List<int>();
            for (var index = 0; index < _occurrences.Count; index++)
            {
                var occurrence = _occurrences[index];
                while (ancestors.Count != 0)
                {
                    var candidate = _occurrences[ancestors[^1]];
                    if (ReferenceEquals(candidate.Node.SyntaxTree, occurrence.Node.SyntaxTree)
                        && candidate.FirstToken <= occurrence.FirstToken
                        && occurrence.LastToken <= candidate.LastToken
                        && (candidate.FirstToken != occurrence.FirstToken
                            || candidate.LastToken != occurrence.LastToken))
                        break;
                    ancestors.RemoveAt(ancestors.Count - 1);
                }

                var parent = ancestors.Count == 0 ? -1 : ancestors[^1];
                _occurrences[index] = occurrence with { Parent = parent };
                ancestors.Add(index);
            }
        }

        void Decide()
        {
            for (var occurrenceIndex = _occurrences.Count - 1; occurrenceIndex >= 0; occurrenceIndex--)
            {
                var occurrence = _occurrences[occurrenceIndex];
                var multi = occurrence.Setting.Preference == ConstructLayoutPreference.Multi;
                var hasLineComment = false;
                var checkedLineComments = false;
                if (occurrence.Setting is
                        { Preference: ConstructLayoutPreference.Auto, Maximum: not int.MaxValue })
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
                        > occurrence.Setting.Maximum;
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
            width = ConstructLayoutPlan.BaseGapWidth(
                _stream.Pieces[index - 1].Token,
                _stream.Pieces[index].Token,
                _trivia!.Trailing(index - 1),
                _trivia.Leading(index),
                _settings.TabWidth,
                _emitterPlan);
            (_baseGapWidths ??= [])[index] = width;
            return width;
        }

        ConstructLayoutRule.Setting? Enabled(ConstructLayoutKind kind) => _byKind[(int)kind];

        bool Unsafe(SyntaxNode node) => node.ContainsDirectives
            || ReferenceEquals(node.SyntaxTree, _root.SyntaxTree)
            && _safetyContext?.IsUnsafe(node) == true;

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

        internal SparseGapWidths(List<Boundary> boundaries)
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

    readonly record struct Boundary(int RightIndex, GapStyle Style);

    enum GapStyle
    {
        DelimitedFirst,
        DelimitedLater,
        DelimitedClose,
        SeparatedFirst,
        SeparatedLater,
        Item,
        CompactItem
    }

    readonly record struct Occurrence(
        SyntaxNode Node,
        ConstructLayoutRule.Setting Setting,
        int BoundaryStart,
        int BoundaryCount,
        int Parent,
        int FirstToken,
        int LastToken,
        bool Applies = false,
        bool Multi = false);

    static int BaseGapWidth(
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

    static string Render(
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
                return lineEnding + (boundary.Style == GapStyle.DelimitedClose ? baseIndent : indent);
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
            case GapStyle.DelimitedLater:
                left = Trailing(leftTrailing, multi ? line : space);
                right = Leading(rightLeading, default);
                break;
            case GapStyle.DelimitedClose:
                left = WithoutWhitespace(leftTrailing);
                right = Leading(rightLeading, multi ? baseLine : default);
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

    static int PlannedWidth(
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
        GapStyle.DelimitedLater
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

    static int VisualWidth(string text, int tabWidth)
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

    static int VisualWidth(
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

readonly record struct ConstructTriggerMask(
    bool OpenParen,
    bool OpenBracket,
    bool OpenBrace,
    bool Colon,
    bool Where,
    bool From,
    bool Member,
    bool Question,
    bool Binary)
{
    internal static ConstructTriggerMask For(ConstructLayoutRule.Setting?[] byKind)
    {
        bool Enabled(ConstructLayoutKind kind) => byKind[(int)kind] is not null;
        var arguments = Enabled(ConstructLayoutKind.Arguments);
        var parameters = Enabled(ConstructLayoutKind.Parameters);
        var member = Enabled(ConstructLayoutKind.MemberAccessChains);
        return new(
            arguments || parameters,
            arguments
            || parameters
            || Enabled(ConstructLayoutKind.CollectionExpressions)
            || Enabled(ConstructLayoutKind.Attributes),
            Enabled(ConstructLayoutKind.Initializers),
            Enabled(ConstructLayoutKind.BaseTypeLists),
            Enabled(ConstructLayoutKind.ConstraintClauses),
            Enabled(ConstructLayoutKind.QueryClauses),
            member,
            member || Enabled(ConstructLayoutKind.ConditionalExpressions),
            Enabled(ConstructLayoutKind.BinaryExpressions));
    }

    internal bool Matches(SyntaxKind kind) => kind switch
        {
            SyntaxKind.OpenParenToken => OpenParen,
            SyntaxKind.OpenBracketToken => OpenBracket,
            SyntaxKind.OpenBraceToken => OpenBrace,
            SyntaxKind.ColonToken => Colon,
            SyntaxKind.WhereKeyword => Where,
            SyntaxKind.FromKeyword => From,
            SyntaxKind.DotToken or SyntaxKind.MinusGreaterThanToken => Member,
            SyntaxKind.QuestionToken => Question,
            _ => Binary && IsBinaryOperator(kind)
        };

    internal static bool IsBinaryOperator(SyntaxKind kind) => kind is
        SyntaxKind.PlusToken
        or SyntaxKind.MinusToken
        or SyntaxKind.AsteriskToken
        or SyntaxKind.SlashToken
        or SyntaxKind.PercentToken
        or SyntaxKind.AmpersandToken
        or SyntaxKind.BarToken
        or SyntaxKind.CaretToken
        or SyntaxKind.LessThanLessThanToken
        or SyntaxKind.GreaterThanGreaterThanToken
        or SyntaxKind.GreaterThanGreaterThanGreaterThanToken
        or SyntaxKind.EqualsEqualsToken
        or SyntaxKind.ExclamationEqualsToken
        or SyntaxKind.LessThanToken
        or SyntaxKind.LessThanEqualsToken
        or SyntaxKind.GreaterThanToken
        or SyntaxKind.GreaterThanEqualsToken
        or SyntaxKind.AmpersandAmpersandToken
        or SyntaxKind.BarBarToken
        or SyntaxKind.QuestionQuestionToken
        or SyntaxKind.IsKeyword
        or SyntaxKind.AsKeyword;
}
