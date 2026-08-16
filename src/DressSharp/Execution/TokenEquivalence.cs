using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DressSharp.Execution;

/// <summary>
/// Checks that formatting changed only whitespace, by re-reading the result and comparing what the
/// compiler sees.
/// </summary>
/// <remarks>
/// A formatter that emits text rather than editing a tree can silently drop or join tokens, and a
/// byte comparison against a previous release only catches that where a previous release exists.
/// Comparing token streams catches it everywhere, on any file, which is what makes it safe to move
/// rules onto the emitter one at a time.
/// </remarks>
static class TokenEquivalence
{
    /// <summary>
    /// Describes the first difference between the tokens of <paramref name="original"/> and those of
    /// <paramref name="formatted"/>, or null when the two agree.
    /// </summary>
    internal static string? FirstDifference(SyntaxNode original, string formatted, CSharpParseOptions options)
    {
        var rewritten = CSharpSyntaxTree.ParseText(formatted, options).GetRoot();

        using var before = original.DescendantTokens().GetEnumerator();
        using var after = rewritten.DescendantTokens().GetEnumerator();
        var index = 0;
        while (true)
        {
            var hasBefore = MoveToSignificant(before);
            var hasAfter = MoveToSignificant(after);
            if (!hasBefore && !hasAfter)
                return null;
            if (!hasBefore)
                return $"token {index}: formatting added '{after.Current.Text}'";
            if (!hasAfter)
                return $"token {index}: formatting dropped '{before.Current.Text}'";

            if (before.Current.RawKind != after.Current.RawKind || before.Current.Text != after.Current.Text)
            {
                return $"token {index} at line {Line(before.Current)}: "
                    + $"'{before.Current.Text}' ({before.Current.Kind()}) became "
                    + $"'{after.Current.Text}' ({after.Current.Kind()})";
            }

            index++;
        }
    }

    /// <summary>
    /// Advances past tokens of zero width, which the parser inserts where it is recovering and which
    /// therefore say more about the source being malformed than about what formatting did.
    /// </summary>
    static bool MoveToSignificant(IEnumerator<SyntaxToken> tokens)
    {
        while (tokens.MoveNext())
        {
            if (!tokens.Current.IsMissing && tokens.Current.Span.Length > 0)
                return true;
        }

        return false;
    }

    static int Line(SyntaxToken token) => token.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
}
