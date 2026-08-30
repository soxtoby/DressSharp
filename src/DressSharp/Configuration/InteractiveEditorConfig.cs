using System.Collections.Immutable;
using System.Text;
using DressSharp.Architecture;
using DressSharp.IO;
using DressSharp.Rules;

namespace DressSharp.Configuration;

enum PreferenceAssignmentKind
{
    Absent,
    Unset,
    Explicit
}

sealed record PreferenceAssignment(PreferenceAssignmentKind Kind, string? Value = null)
{
    internal static PreferenceAssignment Absent { get; } = new(PreferenceAssignmentKind.Absent);
    internal static PreferenceAssignment Unset { get; } = new(PreferenceAssignmentKind.Unset);
    internal static PreferenceAssignment Explicit(string value) => new(PreferenceAssignmentKind.Explicit, value);
}

sealed record InteractivePreference(
    RuleKey RuleKey,
    PreferenceAssignment Local,
    PreferenceAssignment Inherited,
    string? EffectiveValue);

sealed record InteractiveEditorConfigData(
    string TargetPath,
    string InteractiveRoot,
    ImmutableArray<InteractivePreference> Preferences);

sealed record InteractivePreferenceEdit(RuleKey RuleKey, PreferenceAssignment DesiredLocal);

static class InteractiveEditorConfig
{
    const int MaximumWriteAttempts = 3;

    internal static async ValueTask<InteractiveEditorConfigData> LoadAsync(
        string? target,
        string invocationDirectory,
        CancellationToken cancellationToken = default)
    {
        var targetPath = ResolveTarget(target, invocationDirectory);
        return await LoadResolvedAsync(targetPath, cancellationToken);
    }

    internal static async ValueTask<InteractiveEditorConfigData> MergeAsync(
        InteractiveEditorConfigData loaded,
        IReadOnlyList<InteractivePreferenceEdit> edits,
        CancellationToken cancellationToken = default)
    {
        ValidateEdits(edits);
        if (edits.Count == 0)
            return loaded;

        for (var attempt = 1; attempt <= MaximumWriteAttempts; attempt++)
        {
            var document = await EditorConfigDocument.ReadAsync(loaded.TargetPath, cancellationToken);
            var text = document.Merge(edits);
            try
            {
                await new AtomicFilePersistence().WriteIfChangedAsync(
                    document.Source,
                    document.Source.Encode(text, new()),
                    cancellationToken);
                return await LoadResolvedAsync(loaded.TargetPath, cancellationToken);
            }
            catch (ConcurrentFileChangeException) when (File.Exists(loaded.TargetPath))
            {
                if (attempt == MaximumWriteAttempts)
                    break;
            }
        }

        throw new ConfigurationException($"Could not save '{loaded.TargetPath}' because it kept changing.");
    }

    static async ValueTask<InteractiveEditorConfigData> LoadResolvedAsync(
        string targetPath,
        CancellationToken cancellationToken)
    {
        var documents = await ReadChain(targetPath, cancellationToken);
        var target = documents[0];
        var inherited = new Dictionary<RuleKey, PreferenceAssignment>();
        var effective = new Dictionary<RuleKey, string>();
        for (var index = documents.Count - 1; index >= 1; index--)
            Apply(documents[index].Assignments, inherited, effective);

        var local = target.Assignments;
        foreach (var (key, assignment) in local)
        {
            if (assignment.Kind == PreferenceAssignmentKind.Explicit && !PreferenceCatalog.IsValid(key, assignment.Value!))
                throw InvalidValue(target.Path, key, assignment.Value!);
        }
        Apply(local, null, effective);
        foreach (var (key, value) in effective)
        {
            if (!PreferenceCatalog.IsValid(key, value))
                throw InvalidValue(target.Path, key, value);
        }
        ApplyEditorConfigDerivations(effective);

        var preferences = RuleCatalog.BuiltIn.Rules
            .Select(rule =>
            {
                var key = rule.Metadata.RuleKey;
                return new InteractivePreference(
                    key,
                    local.GetValueOrDefault(key, PreferenceAssignment.Absent),
                    inherited.GetValueOrDefault(key, PreferenceAssignment.Absent),
                    effective.GetValueOrDefault(key));
            })
            .ToImmutableArray();
        return new InteractiveEditorConfigData(
            target.Path,
            Path.GetDirectoryName(target.Path)!,
            preferences);
    }

    static async ValueTask<List<EditorConfigDocument>> ReadChain(
        string targetPath,
        CancellationToken cancellationToken)
    {
        var documents = new List<EditorConfigDocument>();
        var path = targetPath;
        while (true)
        {
            var document = await EditorConfigDocument.ReadAsync(path, cancellationToken);
            documents.Add(document);
            if (document.IsRoot)
                break;
            var parent = Directory.GetParent(Path.GetDirectoryName(path)!);
            if (parent is null)
                break;
            path = Path.Combine(parent.FullName, ".editorconfig");
            while (!File.Exists(path))
            {
                parent = parent.Parent;
                if (parent is null)
                    return documents;
                path = Path.Combine(parent.FullName, ".editorconfig");
            }
        }
        return documents;
    }

    static void Apply(
        IReadOnlyDictionary<RuleKey, PreferenceAssignment> assignments,
        Dictionary<RuleKey, PreferenceAssignment>? states,
        Dictionary<RuleKey, string> effective)
    {
        foreach (var (key, assignment) in assignments)
        {
            states?[key] = assignment;
            if (assignment.Kind == PreferenceAssignmentKind.Unset)
                effective.Remove(key);
            else if (assignment.Kind == PreferenceAssignmentKind.Explicit)
                effective[key] = assignment.Value!;
        }
    }

    static void ApplyEditorConfigDerivations(Dictionary<RuleKey, string> effective)
    {
        if (effective.TryGetValue(RuleKey.IndentSize, out var indentSize))
        {
            if (indentSize.Equals("tab", StringComparison.OrdinalIgnoreCase)
                && effective.TryGetValue(RuleKey.TabWidth, out var tabWidth))
                effective[RuleKey.IndentSize] = tabWidth;
            else if (!effective.ContainsKey(RuleKey.TabWidth))
                effective[RuleKey.TabWidth] = indentSize;
        }
        else if (effective.GetValueOrDefault(RuleKey.IndentStyle)?.Equals("tab", StringComparison.OrdinalIgnoreCase) is true)
            effective[RuleKey.IndentSize] = "tab";
    }

    static void ValidateEdits(IReadOnlyList<InteractivePreferenceEdit> edits)
    {
        if (edits.Select(edit => edit.RuleKey).Distinct().Count() != edits.Count)
            throw new ArgumentException("Preference edits must have unique rule keys.", nameof(edits));
        foreach (var edit in edits)
        {
            var assignment = edit.DesiredLocal;
            if (!Enum.IsDefined(assignment.Kind))
                throw new ArgumentException("Unknown preference assignment state.", nameof(edits));
            if (assignment.Kind == PreferenceAssignmentKind.Explicit)
            {
                if (string.IsNullOrWhiteSpace(assignment.Value)
                    || !PreferenceCatalog.IsValid(edit.RuleKey, assignment.Value))
                    throw new ArgumentException($"Invalid value for '{edit.RuleKey.ToName()}'.", nameof(edits));
            }
            else if (assignment.Value is not null)
                throw new ArgumentException("Only explicit assignments may carry a value.", nameof(edits));
        }
    }

    static string ResolveTarget(string? target, string invocationDirectory)
    {
        if (!string.IsNullOrWhiteSpace(target))
        {
            var path = Path.GetFullPath(target, invocationDirectory);
            if (Directory.Exists(path))
                path = Path.Combine(path, ".editorconfig");
            if (!File.Exists(path))
                throw new ConfigurationException($"EditorConfig '{path}' does not exist.");
            return path;
        }

        var directory = new DirectoryInfo(Path.GetFullPath(invocationDirectory));
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, ".editorconfig");
            if (File.Exists(path))
                return path;
            directory = directory.Parent;
        }
        throw new ConfigurationException($"No .editorconfig was found from '{invocationDirectory}'.");
    }

    static ConfigurationException InvalidValue(string path, RuleKey key, string value) =>
        new($"{path}: invalid effective value '{value}' for '{key.ToName()}'.");
}

sealed class EditorConfigDocument
{
    readonly List<EditorConfigLine> _lines;

    EditorConfigDocument(string path, SourceDocument source, List<EditorConfigLine> lines)
    {
        Path = path;
        Source = source;
        _lines = lines;
        IsRoot = ReadRoot(lines);
        Assignments = ReadAssignments(lines);
    }

    internal string Path { get; }
    internal SourceDocument Source { get; }
    internal bool IsRoot { get; }
    internal IReadOnlyDictionary<RuleKey, PreferenceAssignment> Assignments { get; }

    internal static async ValueTask<EditorConfigDocument> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            throw new ConfigurationException($"EditorConfig '{path}' does not exist.");
        SourceDocument source;
        try
        {
            source = await SourceDocument.ReadAsync(path, cancellationToken);
        }
        catch (SourceIOException exception)
        {
            throw new ConfigurationException(exception.Message);
        }
        if (source.SourceEncoding is not (SourceEncoding.Utf8 or SourceEncoding.Utf8Bom))
            throw new ConfigurationException($"{path}: EditorConfig files must be UTF-8 encoded.");
        EditorConfigSyntaxValidator.Validate(path, source.Text);
        return new EditorConfigDocument(path, source, ParseLines(source.Text));
    }

    internal string Merge(IReadOnlyList<InteractivePreferenceEdit> edits)
    {
        var lines = _lines.Select(line => line with { }).ToList();
        var occurrences = FindOccurrences(lines);
        var removals = new HashSet<int>();
        foreach (var edit in edits)
        {
            var matches = occurrences.GetValueOrDefault(edit.RuleKey, []);
            if (edit.DesiredLocal.Kind == PreferenceAssignmentKind.Absent)
            {
                foreach (var match in matches)
                    removals.Add(match);
            }
            else if (matches.Count > 0)
                lines[matches[^1]].Content = ReplaceValue(lines[matches[^1]].Content, edit.DesiredLocal);
        }
        foreach (var index in removals.OrderDescending())
            lines.RemoveAt(index);

        var remaining = FindOccurrences(lines);
        var additions = edits
            .Where(edit => edit.DesiredLocal.Kind != PreferenceAssignmentKind.Absent
                && !remaining.ContainsKey(edit.RuleKey))
            .OrderBy(edit => CatalogIndex(edit.RuleKey))
            .ToArray();
        var hadFinalNewline = Source.Text.EndsWith('\r') || Source.Text.EndsWith('\n');
        if (additions.Length > 0)
            Insert(lines, additions, hadFinalNewline);
        RestoreFinalNewline(lines, hadFinalNewline);
        return string.Concat(lines.Select(line => line.Content + line.Ending));
    }

    static Dictionary<RuleKey, PreferenceAssignment> ReadAssignments(IReadOnlyList<EditorConfigLine> lines)
    {
        var result = new Dictionary<RuleKey, PreferenceAssignment>();
        var exactSection = false;
        foreach (var line in lines)
        {
            var trimmed = line.Content.Trim();
            if (IsSection(trimmed))
            {
                exactSection = trimmed == "[*.cs]";
                continue;
            }
            if (!exactSection || !TryAssignment(line.Content, out var key, out var value))
                continue;
            result[key] = value.Equals("unset", StringComparison.OrdinalIgnoreCase)
                ? PreferenceAssignment.Unset
                : PreferenceAssignment.Explicit(PreferenceCatalog.Normalize(key, value));
        }
        return result;
    }

    static Dictionary<RuleKey, List<int>> FindOccurrences(IReadOnlyList<EditorConfigLine> lines)
    {
        var result = new Dictionary<RuleKey, List<int>>();
        var exactSection = false;
        for (var index = 0; index < lines.Count; index++)
        {
            var trimmed = lines[index].Content.Trim();
            if (IsSection(trimmed))
            {
                exactSection = trimmed == "[*.cs]";
                continue;
            }
            if (!exactSection || !TryAssignment(lines[index].Content, out var key, out _))
                continue;
            if (!result.TryGetValue(key, out var matches))
                result[key] = matches = [];
            matches.Add(index);
        }
        return result;
    }

    static bool ReadRoot(IEnumerable<EditorConfigLine> lines)
    {
        foreach (var line in lines)
        {
            var trimmed = line.Content.Trim();
            if (IsSection(trimmed))
                return false;
            var separator = trimmed.IndexOf('=');
            if (separator > 0
                && trimmed[..separator].Trim().Equals("root", StringComparison.OrdinalIgnoreCase))
                return trimmed[(separator + 1)..].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    static bool TryAssignment(string line, out RuleKey key, out string value)
    {
        var separator = line.IndexOf('=');
        if (separator > 0 && RuleKeys.TryParse(line[..separator].Trim(), out key))
        {
            value = line[(separator + 1)..].Trim();
            return true;
        }
        key = default;
        value = string.Empty;
        return false;
    }

    static string ReplaceValue(string line, PreferenceAssignment assignment)
    {
        var separator = line.IndexOf('=');
        var valueStart = separator + 1;
        while (valueStart < line.Length && char.IsWhiteSpace(line[valueStart]))
            valueStart++;
        var valueEnd = line.Length;
        while (valueEnd > valueStart && char.IsWhiteSpace(line[valueEnd - 1]))
            valueEnd--;
        var value = assignment.Kind == PreferenceAssignmentKind.Unset
            ? "unset"
            : PreferenceCatalog.Normalize(RuleKeys.Parse(line[..separator].Trim()), assignment.Value!);
        return line[..valueStart] + value + line[valueEnd..];
    }

    static void Insert(
        List<EditorConfigLine> lines,
        IReadOnlyList<InteractivePreferenceEdit> additions,
        bool hadFinalNewline)
    {
        var exactHeader = lines.FindLastIndex(line => line.Content.Trim() == "[*.cs]");
        var newline = PreferredNewline(lines, exactHeader);
        if (exactHeader < 0)
        {
            if (lines.Count > 0 && lines[^1].Content.Length > 0)
                Append(lines, string.Empty, newline);
            Append(lines, "[*.cs]", newline);
            exactHeader = lines.Count - 1;
        }

        var insertionIndex = lines.FindIndex(exactHeader + 1, line => IsSection(line.Content.Trim()));
        if (insertionIndex < 0)
            insertionIndex = lines.Count;
        while (insertionIndex > exactHeader + 1 && string.IsNullOrWhiteSpace(lines[insertionIndex - 1].Content))
            insertionIndex--;
        newline = PreferredNewline(lines, insertionIndex - 1);
        EnsureBoundary(lines, insertionIndex, newline);
        foreach (var edit in additions)
        {
            var value = edit.DesiredLocal.Kind == PreferenceAssignmentKind.Unset
                ? "unset"
                : PreferenceCatalog.Normalize(edit.RuleKey, edit.DesiredLocal.Value!);
            lines.Insert(insertionIndex++, new EditorConfigLine($"{edit.RuleKey.ToName()} = {value}", newline));
        }
        if (insertionIndex == lines.Count && !hadFinalNewline)
            lines[^1].Ending = string.Empty;
    }

    static void Append(List<EditorConfigLine> lines, string content, string newline)
    {
        EnsureBoundary(lines, lines.Count, newline);
        lines.Add(new EditorConfigLine(content, newline));
    }

    static void EnsureBoundary(List<EditorConfigLine> lines, int index, string newline)
    {
        if (index > 0 && lines[index - 1].Ending.Length == 0)
            lines[index - 1].Ending = newline;
    }

    static void RestoreFinalNewline(List<EditorConfigLine> lines, bool hadFinalNewline)
    {
        if (lines.Count == 0)
            return;
        if (!hadFinalNewline)
            lines[^1].Ending = string.Empty;
        else if (lines[^1].Ending.Length == 0)
            lines[^1].Ending = PreferredNewline(lines, lines.Count - 1);
    }

    static string PreferredNewline(IReadOnlyList<EditorConfigLine> lines, int near)
    {
        if (near >= 0 && lines[near].Ending.Length > 0)
            return lines[near].Ending;
        for (var index = Math.Max(0, near); index < lines.Count; index++)
        {
            if (lines[index].Ending.Length > 0)
                return lines[index].Ending;
        }
        return lines.FirstOrDefault(line => line.Ending.Length > 0)?.Ending ?? Environment.NewLine;
    }

    static int CatalogIndex(RuleKey key)
    {
        for (var index = 0; index < RuleCatalog.BuiltIn.Rules.Length; index++)
        {
            if (RuleCatalog.BuiltIn.Rules[index].Metadata.RuleKey == key)
                return index;
        }
        throw new ArgumentOutOfRangeException(nameof(key));
    }
    static bool IsSection(string line) => line.Length >= 2 && line[0] == '[' && line[^1] == ']';

    static List<EditorConfigLine> ParseLines(string text)
    {
        var lines = new List<EditorConfigLine>();
        for (var start = 0; start < text.Length;)
        {
            var end = text.IndexOfAny(['\r', '\n'], start);
            if (end < 0)
            {
                lines.Add(new EditorConfigLine(text[start..], string.Empty));
                break;
            }
            var ending = text[end] == '\r' ? "\r\n" : "\n";
            lines.Add(new EditorConfigLine(text[start..end], ending));
            start = end + ending.Length;
        }
        return lines;
    }
}

sealed record EditorConfigLine(string OriginalContent, string OriginalEnding)
{
    internal string Content { get; set; } = OriginalContent;
    internal string Ending { get; set; } = OriginalEnding;
}
