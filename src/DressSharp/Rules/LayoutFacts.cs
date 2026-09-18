using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DressSharp.Rules;

/// <summary>Physical layout observed by a formatting stage, before that stage's edits.</summary>
abstract class LayoutFacts
{
    internal static LayoutFacts Syntax { get; } = new SyntaxFacts();

    internal abstract int StartLine(SyntaxToken token);
    internal abstract string LeadingIndent(SyntaxToken token);
    internal virtual string TokenText(SyntaxToken token) => token.Text;

    sealed class SyntaxFacts : LayoutFacts
    {
        internal override int StartLine(SyntaxToken token) =>
            token.SyntaxTree!.GetText().Lines.GetLineFromPosition(token.SpanStart).LineNumber;

        internal override string LeadingIndent(SyntaxToken token)
        {
            var text = token.SyntaxTree!.GetText();
            var line = text.Lines.GetLineFromPosition(token.SpanStart);
            var end = line.Start;
            while (end < line.End && text[end] is ' ' or '\t')
                end++;
            return text.ToString(TextSpan.FromBounds(line.Start, end));
        }
    }
}
