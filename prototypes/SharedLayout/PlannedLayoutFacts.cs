using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

// The projected tree remains necessary for consumers not yet migrated. Its positions are
// deliberately ignored here: token identities map directly to the resolved layout.
sealed class PlannedLayoutFacts : LayoutFacts
{
    readonly ResolvedTokenLayout _layout;
    readonly Dictionary<SyntaxToken, int>? _indices;
    readonly EffectiveTokenStream? _stream;
    readonly Dictionary<SyntaxToken, int> _rewritten = [];
    internal int PlannedReads { get; private set; }
    internal int SyntaxReads { get; private set; }

    internal PlannedLayoutFacts(ResolvedTokenLayout layout, IEnumerable<SyntaxToken> tokens)
    {
        _layout = layout;
        _indices = [];
        var index = 0;
        foreach (var token in tokens)
        {
            if (token.RawKind == 0) continue;
            if (index >= layout.Pieces.Length || token.RawKind != layout.Pieces[index].Token.RawKind)
                throw new InvalidOperationException("Layout fact token order changed");
            _indices.Add(token, index++);
        }
        if (index != layout.Pieces.Length) throw new InvalidOperationException("Layout fact token count changed");
    }

    internal PlannedLayoutFacts(ResolvedTokenLayout layout, EffectiveTokenStream stream)
    {
        if (stream.Pieces.Length != layout.Pieces.Length || stream.Pieces.Any(piece => !piece.IsOriginal))
            throw new InvalidOperationException("Expected projected stream without replacements");
        _layout = layout;
        _stream = stream;
    }

    bool Find(SyntaxToken token, out int index)
    {
        if (_rewritten.TryGetValue(token, out index))
        {
            PlannedReads++;
            return true;
        }
        if (_stream is not null ? _stream.TryIndexOfOriginalTarget(token, out index) : _indices!.TryGetValue(token, out index))
        {
            PlannedReads++;
            return true;
        }
        // Generated tokens have no prior extent. Keep this fallback visible to verification;
        // the eligible corpus must not need it for the currently migrated consumers.
        SyntaxReads++;
        return false;
    }

    internal void RegisterRewrites(SyntaxRewritePlan rewrites)
    {
        var pieces = _stream!.Pieces;
        var cursor = 0;
        foreach (var replacement in rewrites.Replacements)
        {
            while (cursor < pieces.Length && pieces[cursor].Token.SpanStart < replacement.Original.Start) cursor++;
            foreach (var token in replacement.Rewritten.DescendantTokens())
            {
                if (token.Parent is BlockSyntax block && block.HasAnnotation(GeneratedSyntax.Block)
                    && (token == block.OpenBraceToken || token == block.CloseBraceToken)) continue;
                if (cursor >= pieces.Length || !replacement.Original.Contains(pieces[cursor].Token.SpanStart)
                    || token.RawKind != pieces[cursor].Token.RawKind || token.Text != pieces[cursor].Token.Text)
                    throw new InvalidOperationException("Brace rewrite token correspondence changed");
                _rewritten.Add(token, cursor++);
            }
            if (cursor < pieces.Length && replacement.Original.Contains(pieces[cursor].Token.SpanStart))
                throw new InvalidOperationException("Brace rewrite lost original tokens");
        }
    }

    internal override int StartLine(SyntaxToken token) =>
        Find(token, out var index) ? _layout.Extent(index).StartLine : Syntax.StartLine(token);

    internal override string LeadingIndent(SyntaxToken token) =>
        Find(token, out var index) ? _layout.LeadingIndent(index) : Syntax.LeadingIndent(token);

    internal override string TokenText(SyntaxToken token) =>
        Find(token, out var index) ? _layout.TokenText(index) : token.Text;
}
