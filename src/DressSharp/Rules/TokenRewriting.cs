using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DressSharp.Rules;

static class TokenRewriting
{
    /// <summary>
    /// Above this many replacements a rewriter beats <see cref="SyntaxNode.ReplaceTokens"/>; below it
    /// the span pruning that method does is the cheaper way to reach a handful of scattered tokens.
    /// </summary>
    const int RewriterThreshold = 8;

    /// <summary>
    /// Replaces tokens throughout a tree.
    /// </summary>
    /// <remarks>
    /// Roslyn's own <see cref="SyntaxNode.ReplaceTokens"/> decides whether to descend into a node by
    /// testing it against every replaced span in turn, so a rule that rewrites a large share of a
    /// file pays time proportional to nodes times replacements. A rewriter visits each node once and
    /// answers from a hash lookup, which is what a whole-file rule actually wants.
    /// </remarks>
    internal static SyntaxNode ReplaceTokens(SyntaxNode root, Dictionary<SyntaxToken, SyntaxToken> replacements) =>
        replacements.Count switch
            {
                0 => root,
                <= RewriterThreshold => root.ReplaceTokens(replacements.Keys, (token, _) => replacements[token]),
                _ => new Rewriter(replacements).Visit(root)!
            };

    sealed class Rewriter(Dictionary<SyntaxToken, SyntaxToken> replacements) : CSharpSyntaxRewriter
    {
        public override SyntaxToken VisitToken(SyntaxToken token) => replacements.GetValueOrDefault(token, token);
    }
}
