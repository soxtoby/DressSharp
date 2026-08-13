using System.Text;

namespace DressSharp.Configuration;

internal sealed record InitializationResult(string Path, bool Changed, IReadOnlyList<string> Warnings);

internal static class EditorConfigInitializer
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

        var prefix = string.Join(newline, outside).TrimEnd('\r', '\n');
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

    private static string ResolveTarget(string? target, string invocationDirectory)
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

    private static List<string> SplitLines(string text) => text.Length == 0
        ? []
        : text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n').Split('\n').ToList();

    private static string DetectNewline(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    private static List<int> FindMarkers(List<string> lines, string marker) => lines.Select((line, index) => (line, index)).Where(x => x.line == marker).Select(x => x.index).ToList();

    private static void ValidateMarkers(string path, List<int> begin, List<int> end)
    {
        if (begin.Count > 1 || end.Count > 1 || begin.Count != end.Count || (begin.Count == 1 && begin[0] >= end[0]))
            throw new ConfigurationException($"{path}: malformed or duplicate DressSharp managed markers.");
    }

    private static List<string> RemoveManagedBlock(List<string> lines, List<int> begin, List<int> end)
    {
        if (begin.Count == 0)
            return lines;
        var result = lines.Take(begin[0]).Concat(lines.Skip(end[0] + 1)).ToList();
        while (result.Count > 0 && string.IsNullOrWhiteSpace(result[^1]))
        {
            result.RemoveAt(result.Count - 1);
        }
        return result;
    }

    private static List<string> FindConflicts(string path, List<string> lines)
    {
        var conflicts = new List<string>();
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0 || line[0] is '#' or ';' or '[')
                continue;
            var separator = line.IndexOfAny(['=', ':']);
            if (separator <= 0)
                continue;
            var key = line[..separator].Trim();
            if (PreferenceCatalog.Familiar.ContainsKey(key))
                conflicts.Add($"{path}({index + 1}): {key}");
        }
        return conflicts;
    }
}
