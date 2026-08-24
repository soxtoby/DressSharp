using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DressSharp.Rules;

/// <summary>
/// The effective token stream after member rewrites, materialized once for planning and emission.
/// </summary>
sealed class EffectiveTokenStream
{
    readonly Segment[] _replacementSegments;

    EffectiveTokenStream(Piece[] pieces, Segment[] replacementSegments, int[] triggerIndices)
    {
        Pieces = pieces;
        _replacementSegments = replacementSegments;
        TriggerIndices = triggerIndices;
    }

    internal Piece[] Pieces { get; }
    internal int[] TriggerIndices { get; }

    internal static EffectiveTokenStream For(
        SyntaxNode root,
        string source,
        SyntaxRewritePlan rewrites)
    {
        var pieces = new List<Piece>();
        List<Segment>? segments = null;
        var replacements = rewrites.Replacements;
        var next = 0;
        foreach (var token in root.DescendantTokens())
        {
            var start = token.FullSpan.Start;
            while (next < replacements.Count && start >= replacements[next].Original.End)
                next++;
            
            if (next < replacements.Count && replacements[next].Original.Contains(start))
            {
                if (start == replacements[next].Original.Start)
                {
                    var replacement = replacements[next];
                    var replacementStart = pieces.Count;
                    var segmentIndex = segments?.Count ?? 0;
                    pieces.AddRange(replacement.Rewritten.DescendantTokens()
                        .Select(rewritten => new Piece(rewritten,
                            replacement.Text,
                            false,
                            replacement.Original.Start,
                            segmentIndex)));
                    (segments ??= []).Add(new(
                        replacement.Original,
                        replacementStart,
                        pieces.Count - replacementStart));
                }
            }
            else
            {
                pieces.Add(new(token, source, true, token.SpanStart, -1));
            }
        }

        return new(
            pieces.ToArray(),
            segments?.ToArray() ?? [],
            []);
    }

    internal static EffectiveTokenStream ForSyntaxWrapping(
        SyntaxNode root,
        string source,
        SyntaxRewritePlan rewrites,
        SyntaxWrappingTriggerMask triggerMask)
    {
        var pieces = new List<Piece>();
        List<Segment>? segments = null;
        List<int>? triggerIndices = null;
        var replacements = rewrites.Replacements;
        var next = 0;
        foreach (var token in root.DescendantTokens())
        {
            var start = token.FullSpan.Start;
            while (next < replacements.Count && start >= replacements[next].Original.End)
                next++;
            
            if (next < replacements.Count && replacements[next].Original.Contains(start))
            {
                if (start == replacements[next].Original.Start)
                {
                    var replacement = replacements[next];
                    var replacementStart = pieces.Count;
                    var segmentIndex = segments?.Count ?? 0;
                    foreach (var rewritten in replacement.Rewritten.DescendantTokens())
                    {
                        var index = pieces.Count;
                        pieces.Add(new(
                            rewritten,
                            replacement.Text,
                            false,
                            replacement.Original.Start,
                            segmentIndex));
                        if (triggerMask.Matches((SyntaxKind)rewritten.RawKind))
                            (triggerIndices ??= []).Add(index);
                    }

                    (segments ??= []).Add(new(
                        replacement.Original,
                        replacementStart,
                        pieces.Count - replacementStart));
                }
            }
            else
            {
                var index = pieces.Count;
                pieces.Add(new(token, source, true, token.SpanStart, -1));
                if (triggerMask.Matches((SyntaxKind)token.RawKind))
                    (triggerIndices ??= []).Add(index);
            }
        }

        return new(
            pieces.ToArray(),
            segments?.ToArray() ?? [],
            triggerIndices?.ToArray() ?? []);
    }

    internal int IndexOf(SyntaxToken token, int segmentIndex)
    {
        if (segmentIndex < 0)
        {
            var index = OriginalIndex(token);
            if (index >= 0)
                return index;
        }
        else if (segmentIndex < _replacementSegments.Length)
        {
            var segment = _replacementSegments[segmentIndex];
            var low = segment.Start;
            var high = segment.Start + segment.Length;
            while (low < high)
            {
                var middle = low + ((high - low) >> 1);
                if (Pieces[middle].Token.SpanStart < token.SpanStart)
                    low = middle + 1;
                else
                    high = middle;
            }

            for (var index = low;
                index < segment.Start + segment.Length
                    && Pieces[index].Token.SpanStart == token.SpanStart;
                index++)
            {
                if (Pieces[index].Token == token)
                    return index;
            }
        }

        throw new InvalidOperationException($"Token '{token}' is absent from the effective stream.");
    }

    internal bool TryIndexOfOriginalTarget(SyntaxToken token, out int index)
    {
        index = OriginalIndex(token);
        if (index >= 0)
            return true;

        foreach (var segment in _replacementSegments)
        {
            if (segment.Original.Contains(token.SpanStart) && segment.Length != 0)
            {
                index = segment.Start;
                return true;
            }
        }

        index = -1;
        return false;
    }

    int OriginalIndex(SyntaxToken token)
    {
        var low = 0;
        var high = Pieces.Length;
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            if (Pieces[middle].OriginalPosition < token.SpanStart)
                low = middle + 1;
            else
                high = middle;
        }

        for (var index = low;
            index < Pieces.Length && Pieces[index].OriginalPosition == token.SpanStart;
            index++)
        {
            if (Pieces[index].IsOriginal && Pieces[index].Token == token)
                return index;
        }

        return -1;
    }

    internal bool TryGetReplacementSegment(int segmentIndex, out Segment segment)
    {
        if ((uint)segmentIndex < (uint)_replacementSegments.Length)
        {
            segment = _replacementSegments[segmentIndex];
            return true;
        }

        segment = default;
        return false;
    }
    
    internal readonly record struct Piece(
        SyntaxToken Token,
        string Source,
        bool IsOriginal,
        int OriginalPosition,
        int SegmentIndex);
    
    internal readonly record struct Segment(TextSpan Original, int Start, int Length);
}
