using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Docs;

/// <summary>
/// Syntax-only C# highlighting. Tokens are classified from the parse tree, never from semantics, so
/// the site highlights code the same way the formatter reads it.
/// </summary>
static class CSharpHighlighter
{
    /// <summary>Renders <paramref name="code"/> as one HTML string per line, spans never crossing lines.</summary>
    internal static string[] HighlightLines(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Parse));
        var root = tree.GetRoot();
        var spans = new List<(int Start, int End, string Class)>();
        foreach (var token in root.DescendantTokens(descendIntoTrivia: false))
        {
            AddTrivia(token.LeadingTrivia, spans);
            var cls = Classify(token);
            if (cls is not null && token.Span.Length > 0)
                spans.Add((token.Span.Start, token.Span.End, cls));
            AddTrivia(token.TrailingTrivia, spans);
        }
        spans.Sort((a, b) => a.Start.CompareTo(b.Start));

        var lines = new List<string>();
        var line = new StringBuilder();
        var position = 0;
        foreach (var (start, end, cls) in spans)
        {
            if (start < position)
                continue;
            Emit(code, position, start, null, line, lines);
            Emit(code, start, end, cls, line, lines);
            position = end;
        }
        Emit(code, position, code.Length, null, line, lines);
        lines.Add(line.ToString());
        return [.. lines.Select(WhitespaceMarkup)];
    }

    static void Emit(string code, int start, int end, string? cls, StringBuilder line, List<string> lines)
    {
        if (end <= start)
            return;
        var text = code[start..end].Replace("\r\n", "\n").Replace('\r', '\n');
        var parts = text.Split('\n');
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0)
            {
                lines.Add(line.ToString());
                line.Clear();
            }
            if (parts[i].Length == 0)
                continue;
            var escaped = WebUtility.HtmlEncode(parts[i]);
            if (cls is null)
                line.Append(escaped);
            else
                line.Append("<span class=\"").Append(cls).Append("\">").Append(escaped).Append("</span>");
        }
    }

    /// <summary>
    /// Tabs become visible markers, and whitespace running to the end of a line is marked so a trim
    /// rule's work shows. Works on a finished line of HTML: tabs never occur inside markup, and the
    /// trailing run may sit inside a closing span, such as the end of a comment.
    /// </summary>
    static string WhitespaceMarkup(string html)
    {
        var match = TrailingWhitespace.Match(html);
        if (match.Success)
            html = html[..match.Index] + "<span class=\"ws-trail\">" + match.Groups[1].Value + "</span>" + match.Groups[2].Value;
        return html.Replace("\t", "<span class=\"ws-tab\">\t</span>");
    }

    static readonly Regex TrailingWhitespace = new("([ \\t]+)((?:</span>)*)$", RegexOptions.Compiled);

    static void AddTrivia(SyntaxTriviaList trivia, List<(int, int, string)> spans)
    {
        foreach (var piece in trivia)
        {
            var cls = piece.Kind() switch
                {
                    SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineCommentTrivia
                        or SyntaxKind.SingleLineDocumentationCommentTrivia or SyntaxKind.MultiLineDocumentationCommentTrivia
                        or SyntaxKind.DisabledTextTrivia => "c",
                    _ when piece.IsDirective => "d",
                    _ => null
                };
            if (cls is not null && piece.FullSpan.Length > 0)
                spans.Add((piece.FullSpan.Start, piece.FullSpan.End, cls));
        }
    }

    static string? Classify(SyntaxToken token)
    {
        var kind = token.Kind();
        if (SyntaxFacts.IsKeywordKind(kind))
            return "k";
        if (SyntaxFacts.IsContextualKeyword(kind) && token.Parent is not IdentifierNameSyntax)
            return "k";
        return kind switch
            {
                SyntaxKind.StringLiteralToken or SyntaxKind.CharacterLiteralToken or SyntaxKind.InterpolatedStringTextToken
                    or SyntaxKind.InterpolatedStringStartToken or SyntaxKind.InterpolatedStringEndToken
                    or SyntaxKind.InterpolatedVerbatimStringStartToken or SyntaxKind.InterpolatedSingleLineRawStringStartToken
                    or SyntaxKind.InterpolatedMultiLineRawStringStartToken or SyntaxKind.InterpolatedRawStringEndToken
                    or SyntaxKind.SingleLineRawStringLiteralToken or SyntaxKind.MultiLineRawStringLiteralToken
                    or SyntaxKind.Utf8StringLiteralToken or SyntaxKind.Utf8SingleLineRawStringLiteralToken
                    or SyntaxKind.Utf8MultiLineRawStringLiteralToken => "s",
                SyntaxKind.NumericLiteralToken => "n",
                SyntaxKind.IdentifierToken when IsTypeName(token) => "t",
                _ => null
            };
    }

    static bool IsTypeName(SyntaxToken token) => token.Parent switch
        {
            BaseTypeDeclarationSyntax declaration => declaration.Identifier == token,
            DelegateDeclarationSyntax declaration => declaration.Identifier == token,
            GenericNameSyntax => true,
            IdentifierNameSyntax name => name.Parent is SimpleBaseTypeSyntax
                or ObjectCreationExpressionSyntax
                or VariableDeclarationSyntax
                or ParameterSyntax
                or TypeConstraintSyntax
                or AttributeSyntax
                or CastExpressionSyntax
                or ArrayTypeSyntax
                or NullableTypeSyntax
                or TypeArgumentListSyntax
                or MethodDeclarationSyntax
                or PropertyDeclarationSyntax
                or FieldDeclarationSyntax
                or CatchDeclarationSyntax
                or QualifiedNameSyntax { Parent: not MemberAccessExpressionSyntax },
            _ => false
        };
}
