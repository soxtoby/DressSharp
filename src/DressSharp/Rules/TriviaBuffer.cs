using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DressSharp.Rules;

/// <summary>
/// A mutable view of the leading and trailing trivia of one tree's tokens.
/// </summary>
/// <remarks>
/// A rule that only moves whitespace does not change the shape of the tree, so a run of such rules
/// can read and write their edits here against one frozen tree and have them realised as a single
/// rebuild at the end. Read through <see cref="Leading"/> and <see cref="Trailing"/> rather than off
/// the token: a rule earlier in the run may already have changed what the token ought to say, and
/// the tree still shows the original.
/// </remarks>
sealed class TriviaBuffer
{
    readonly Dictionary<SyntaxToken, Edit> _edits = [];

    internal SyntaxTriviaList Leading(SyntaxToken token) =>
        _edits.TryGetValue(token, out var edit) && edit.Leading is { } leading ? leading : token.LeadingTrivia;

    internal SyntaxTriviaList Trailing(SyntaxToken token) =>
        _edits.TryGetValue(token, out var edit) && edit.Trailing is { } trailing ? trailing : token.TrailingTrivia;

    internal void SetLeading(SyntaxToken token, SyntaxTriviaList trivia) =>
        _edits[token] = _edits.TryGetValue(token, out var edit) ? edit with { Leading = trivia } : new Edit(trivia, null);

    internal void SetTrailing(SyntaxToken token, SyntaxTriviaList trivia) =>
        _edits[token] = _edits.TryGetValue(token, out var edit) ? edit with { Trailing = trivia } : new Edit(null, trivia);

    internal SyntaxNode Apply(SyntaxNode root)
    {
        if (_edits.Count == 0)
            return root;

        var edits = new TokenEdits();
        var ordered = new List<KeyValuePair<SyntaxToken, Edit>>(_edits);
        ordered.Sort(static (left, right) => left.Key.FullSpan.Start.CompareTo(right.Key.FullSpan.Start));
        foreach (var (token, edit) in ordered)
        {
            var replacement = token;
            if (edit.Leading is { } leading)
                replacement = replacement.WithLeadingTrivia(leading);
            if (edit.Trailing is { } trailing)
                replacement = replacement.WithTrailingTrivia(trailing);
            edits.Append(token, replacement);
        }

        return edits.Apply(root);
    }

    readonly record struct Edit(SyntaxTriviaList? Leading, SyntaxTriviaList? Trailing);
}
