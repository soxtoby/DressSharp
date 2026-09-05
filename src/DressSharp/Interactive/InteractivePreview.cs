using DressSharp.Architecture;
using DressSharp.Configuration;
using DressSharp.Execution;
using DressSharp.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DressSharp.Interactive;

static class InteractivePreview
{
    internal static async Task<PreviewResult> Format(
        string source,
        IReadOnlyList<InteractivePreference> preferences,
        CancellationToken cancellationToken)
    {
        var configuration = InteractiveEditorConfig.ResolvePending(preferences);
        var options = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Parse, SourceCodeKind.Regular);
        var document = SourceDocument.FromText("Preview.cs", source);
        var result = await new DocumentFormatter(configuration, new BenchmarkTiming()).Format(document, options, cancellationToken);
        var text = SourceDocument.Decode(result.Content, result.Encoding);
        var lineEndings = new List<string>();
        if (text.Contains("\r\n", StringComparison.Ordinal))
            lineEndings.Add("CRLF");
        var withoutCrLf = text.Replace("\r\n", "", StringComparison.Ordinal);
        if (withoutCrLf.Contains('\n'))
            lineEndings.Add("LF");
        if (withoutCrLf.Contains('\r'))
            lineEndings.Add("CR");
        return new PreviewResult(
            text,
            configuration.Preferences.GetValueOrDefault(RuleKey.Charset, "utf-8"),
            lineEndings.Count == 0 ? "None" : string.Join(" / ", lineEndings),
            text.EndsWith('\n') || text.EndsWith('\r'),
            result.SkippedOccurrences,
            options.LanguageVersion.ToDisplayString());
    }
}

sealed record PreviewResult(string Text, string Encoding, string LineEndings, bool FinalNewline, int SkippedOccurrences, string LanguageVersion);
