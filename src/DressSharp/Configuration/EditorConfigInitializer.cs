using System.Text;

namespace DressSharp.Configuration;

sealed record InitializationResult(string Path, bool Changed, IReadOnlyList<string> Warnings);

static class EditorConfigInitializer
{
    internal const string BeginMarker = "# DressSharp Begin";
    internal const string EndMarker = "# DressSharp End";

    internal static async Task<InitializationResult> InitializeAsync(
        string? target, bool force, string invocationDirectory, CancellationToken cancellationToken)
    {
        var path = ResolveTarget(target, invocationDirectory);
        var original = File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : string.Empty;
        if (original.Length > 0)
            EditorConfigSyntaxValidator.Validate(path, original);

        var newline = DetectNewline(original);
        var lines = SplitLines(original);
        var begin = FindMarkers(lines, BeginMarker);
        var end = FindMarkers(lines, EndMarker);
        ValidateMarkers(path, begin, end);

        var outside = RemoveManagedBlock(lines, begin, end);
        var conflicts = FindConflicts(path, outside);
        if (conflicts.Count > 0 && !force)
            throw new ConfigurationException("DressSharp preference conflicts:" + Environment.NewLine + string.Join(Environment.NewLine, conflicts));

        var prefix = string.Join(newline, outside.Select(line => line.Text)).TrimEnd('\r', '\n');
        var block = BuildManagedBlock(newline);
        var output = prefix.Length == 0 ? block : prefix + newline + newline + block;
        if (original == output)
            return new InitializationResult(path, false, []);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, output, new UTF8Encoding(false), cancellationToken);
        return new InitializationResult(path, true, force ? conflicts : []);
    }

    internal static string BuildManagedBlock(string newline = "\n")
    {
        var lines = new List<string> { BeginMarker, "[*.cs]" };
        lines.AddRange(PreferenceCatalog.Familiar.Select(item => $"{item.Key} = {item.Value}"));
        lines.Add(EndMarker);
        return string.Join(newline, lines) + newline;
    }

    static string ResolveTarget(string? target, string invocationDirectory)
    {
        if (string.IsNullOrWhiteSpace(target))
            return Path.Combine(Path.GetFullPath(invocationDirectory), ".editorconfig");
        var full = Path.GetFullPath(target, invocationDirectory);
        if (Directory.Exists(full))
            return Path.Combine(full, ".editorconfig");
        if (File.Exists(full))
            return full;
        var trailingSeparator = target.EndsWith(Path.DirectorySeparatorChar) || target.EndsWith(Path.AltDirectorySeparatorChar);
        return trailingSeparator ? Path.Combine(full, ".editorconfig") : full;
    }

    static List<string> SplitLines(string text) => text.Length == 0
        ? []
        : text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n').Split('\n').ToList();

    static string DetectNewline(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    static List<int> FindMarkers(List<string> lines, string marker) => lines.Select((line, index) => (line, index)).Where(x => x.line == marker).Select(x => x.index).ToList();

    static void ValidateMarkers(string path, List<int> begin, List<int> end)
    {
        if (begin.Count > 1
            || end.Count > 1
            || begin.Count != end.Count
            || (begin.Count == 1 && begin[0] >= end[0]))
        {
            throw new ConfigurationException($"{path}: malformed or duplicate DressSharp managed markers.");
        }
    }

    static List<SourceLine> RemoveManagedBlock(List<string> lines, List<int> begin, List<int> end)
    {
        var numberedLines = lines.Select((text, index) => new SourceLine(text, index + 1)).ToList();
        if (begin.Count == 0)
            return numberedLines;
        var result = numberedLines.Take(begin[0]).Concat(numberedLines.Skip(end[0] + 1)).ToList();
        while (result.Count > 0 && string.IsNullOrWhiteSpace(result[^1].Text))
        {
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }

    static List<string> FindConflicts(string path, List<SourceLine> lines)
    {
        var conflicts = new List<string>();
        foreach (var sourceLine in lines)
        {
            var line = sourceLine.Text.Trim();
            if (line.Length != 0 && line[0] is not ('#' or ';' or '['))
            {
                var separator = line.IndexOfAny(['=', ':']);
                if (separator > 0)
                {
                    var key = line[..separator].Trim();
                    if (PreferenceCatalog.Familiar.ContainsKey(key))
                        conflicts.Add($"{path}({sourceLine.Number}): {key}");
                }
            }
        }

        return conflicts;
    }

    sealed record SourceLine(string Text, int Number);
}