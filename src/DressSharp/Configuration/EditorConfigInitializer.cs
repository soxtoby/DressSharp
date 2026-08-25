using System.Text;
using DressSharp.Architecture;

namespace DressSharp.Configuration;

sealed record InitializationResult(string Path, bool Changed);

static class EditorConfigInitializer
{
    internal static async Task<InitializationResult> InitializeAsync(
        string? target, string invocationDirectory, CancellationToken cancellationToken)
    {
        var path = ResolveTarget(target, invocationDirectory);
        var original = File.Exists(path) 
            ? await File.ReadAllTextAsync(path, cancellationToken) 
            : string.Empty;
        if (original.Length > 0)
            EditorConfigSyntaxValidator.Validate(path, original);

        var newline = DetectNewline(original);
        var lines = SplitLines(original);
        var existing = ExistingPreferences(lines);
        var missing = PreferenceCatalog.Defaults
            .Where(preference => !existing.Contains(preference.Key))
            .ToList();
        if (missing.Count == 0)
            return new InitializationResult(path, false);

        var sectionIndex = lines.FindLastIndex(IsCSharpSection);
        if (sectionIndex < 0)
        {
            if (lines.Count != 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                lines.Add(string.Empty);
            lines.Add("[*.cs]");
            sectionIndex = lines.Count - 1;
        }

        var insertionIndex = lines.FindIndex(sectionIndex + 1, IsSection);
        if (insertionIndex < 0)
            insertionIndex = lines.Count;
        while (insertionIndex > sectionIndex + 1 && string.IsNullOrWhiteSpace(lines[insertionIndex - 1]))
            insertionIndex--;

        lines.InsertRange(insertionIndex, missing.Select(Assignment));
        var output = string.Join(newline, lines) + newline;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, output, new UTF8Encoding(false), cancellationToken);
        return new InitializationResult(path, true);
    }

    internal static string BuildDefaultSection(string newline = "\n") =>
        "[*.cs]" + newline + string.Join(newline, PreferenceCatalog.Defaults.Select(Assignment)) + newline;

    static string Assignment((RuleKey Key, string Default) preference) =>
        $"{preference.Key.ToName()} = {preference.Default}";

    static HashSet<RuleKey> ExistingPreferences(IEnumerable<string> lines)
    {
        var existing = new HashSet<RuleKey>();
        foreach (var sourceLine in lines)
        {
            var line = sourceLine.Trim();
            if (line.Length == 0 || line[0] is '#' or ';' or '[')
                continue;

            var separator = line.IndexOfAny(['=', ':']);
            if (separator > 0 && RuleKeys.TryParse(line[..separator].Trim(), out var key))
                existing.Add(key);
        }

        return existing;
    }

    static bool IsCSharpSection(string line)
    {
        var trimmed = line.TrimStart();
        var close = trimmed.IndexOf(']');
        return close >= 0 && trimmed[0] == '[' && trimmed[1..close].Equals("*.cs", StringComparison.Ordinal);
    }

    static bool IsSection(string line) => line.TrimStart().StartsWith('[');

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
}
