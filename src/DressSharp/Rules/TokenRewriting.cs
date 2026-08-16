using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DressSharp.Rules;

/// <summary>
/// A set of token replacements to apply to a tree in one pass, held in document order.
/// </summary>
/// <remarks>
/// The obvious representation, a dictionary keyed by <see cref="SyntaxToken"/>, turns out to be the
/// single most expensive thing a rewriting rule does: hashing a token hashes the identity of its
/// underlying node, and the apply step does that for every token in the file. Holding the edits in
/// document order instead lets the apply walk advance a cursor alongside the tree and compare tokens
/// directly, so a rewrite costs no hashing at all.
/// </remarks>
sealed class TokenEdits
{
    readonly List<(SyntaxToken Original, SyntaxToken Replacement)> _edits = [];

    internal int Count => _edits.Count;

    /// <summary>
    /// The pending replacement for a token, or the token itself when it has none. Only the most
    /// recently added edit is visible, which is all a caller building edits in document order needs.
    /// </summary>
    internal SyntaxToken Pending(SyntaxToken token) =>
        _edits.Count > 0 && _edits[^1].Original == token ? _edits[^1].Replacement : token;

    /// <summary>
    /// Records a replacement for a token at or after every token recorded so far.
    /// </summary>
    internal void Append(SyntaxToken original, SyntaxToken replacement)
    {
        if (_edits.Count > 0 && _edits[^1].Original == original)
            _edits[^1] = (original, replacement);
        else
            _edits.Add((original, replacement));
    }

    /// <summary>
    /// Records replacements gathered in no particular order, sorting them into document order.
    /// </summary>
    internal static TokenEdits FromUnordered(Dictionary<SyntaxToken, SyntaxToken> replacements)
    {
        var edits = new TokenEdits();
        foreach (var (original, replacement) in replacements)
            edits._edits.Add((original, replacement));
        edits._edits.Sort(static (left, right) => left.Original.FullSpan.Start.CompareTo(right.Original.FullSpan.Start));
        return edits;
    }

    internal SyntaxNode Apply(SyntaxNode root) => _edits.Count == 0 ? root : new Rewriter(_edits).Visit(root)!;

    sealed class Rewriter(List<(SyntaxToken Original, SyntaxToken Replacement)> edits) : CSharpSyntaxRewriter
    {
        int _cursor;

        public override SyntaxToken VisitToken(SyntaxToken token)
        {
            // A rewriter reaches tokens in the same order as a document-order walk, so the cursor
            // only ever moves forwards. Tokens of zero full width can share a start, hence the probe.
            var start = token.FullSpan.Start;
            while (_cursor < edits.Count && edits[_cursor].Original.FullSpan.Start < start)
                _cursor++;

            for (var probe = _cursor; probe < edits.Count && edits[probe].Original.FullSpan.Start == start; probe++)
            {
                if (edits[probe].Original != token)
                    continue;
                if (probe == _cursor)
                    _cursor++;
                return edits[probe].Replacement;
            }

            return token;
        }
    }
}

static class TokenRewriting
{
    internal static SyntaxNode ReplaceTokens(SyntaxNode root, Dictionary<SyntaxToken, SyntaxToken> replacements) =>
        replacements.Count == 0 ? root : TokenEdits.FromUnordered(replacements).Apply(root);
}
