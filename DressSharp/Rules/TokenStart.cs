using Microsoft.CodeAnalysis;

namespace DressSharp.Rules;

/// <summary>
/// A token paired with the position of the token before it, so that "does this node begin here?"
/// is one comparison instead of a walk down the node's left spine.
/// </summary>
/// <remarks>
/// <c>node.GetFirstToken()</c> descends to the first token with width, costing the node's depth and
/// a pooled navigator stack on every call. Emission already visits tokens in order, so the token
/// before this one is known, and tokens partition a node's full span: the node begins at this token
/// exactly when no earlier token with width belongs to the node, which only the nearest one decides.
/// </remarks>
readonly struct TokenStart
{
    readonly int _precedingStart;

    internal TokenStart(SyntaxToken token, int precedingStart)
    {
        Token = token;
        _precedingStart = precedingStart;
    }

    internal SyntaxToken Token { get; }

    /// <summary>
    /// Whether <c>node.GetFirstToken()</c> is this token. The node must contain it.
    /// </summary>
    internal bool Begins(SyntaxNode node) =>
        Token.Span.Length != 0 && node.FullSpan.Start > _precedingStart;
}
