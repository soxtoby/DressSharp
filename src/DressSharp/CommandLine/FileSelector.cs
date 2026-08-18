using Microsoft.Extensions.FileSystemGlobbing;

namespace DressSharp.CommandLine;

sealed class FileSelector(string invocationDirectory)
{
    static readonly HashSet<string> VcsDirectories = new(StringComparer.OrdinalIgnoreCase) { ".git", ".hg", ".svn" };
    static readonly string[] GeneratedSuffixes = [".g.cs", ".generated.cs", ".designer.cs"];
    readonly string _invocationDirectory = Path.GetFullPath(invocationDirectory);

    internal async Task<IReadOnlyList<SelectedFile>> SelectAsync(IReadOnlyList<string> includes, CancellationToken cancellationToken = default)
    {
        var candidates = new Dictionary<string, string>(PathIdentityComparer());
        var patterns = new List<string>();
        foreach (var include in includes.Count == 0 ? [_invocationDirectory] : includes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectUnsupportedInclude(include);
            if (IsGlob(include))
            {
                patterns.Add(NormalizeGlob(include));
                continue;
            }

            var fullPath = Path.GetFullPath(include, _invocationDirectory);
            if (File.Exists(fullPath))
                AddExplicitFile(fullPath, candidates);
            else if (Directory.Exists(fullPath))
                AddDirectory(fullPath, candidates, cancellationToken);
            else
                throw new FileSelectionException($"Path does not exist or is inaccessible: {Display(fullPath)}");
        }

        AddGlobMatches(patterns, candidates, cancellationToken);

        var eligible = candidates.ToArray();
        var generated = new bool[eligible.Length];
        await Parallel.ForAsync(
            0,
            eligible.Length,
            new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                },
            async (index, token) => generated[index] = await IsGeneratedAsync(eligible[index].Key, token));

        return eligible
            .Where((_, index) => !generated[index])
            .Select(pair => new SelectedFile(pair.Key, pair.Value))
            .OrderBy(file => file.DisplayPath, StringComparer.Ordinal)
            .ToArray();
    }

    void AddGlobMatches(
        IReadOnlyList<string> patterns,
        Dictionary<string, string> candidates,
        CancellationToken cancellationToken)
    {
        if (patterns.Count == 0)
            return;

        var discovered = new Dictionary<string, string>(PathIdentityComparer());
        AddDirectory(_invocationDirectory, discovered, cancellationToken);
        var matcher = new Matcher(PathComparison());
        try
        {
            foreach (var pattern in patterns)
                matcher.AddInclude(pattern);
        }
        catch (ArgumentException exception)
        {
            throw new FileSelectionException($"Invalid include glob: {exception.Message}");
        }

        var matches = matcher.Match(discovered.Values).Files
            .Select(match => match.Path)
            .ToHashSet(PathIdentityComparer());
        foreach (var candidate in discovered.Where(candidate => matches.Contains(candidate.Value)))
            candidates.TryAdd(candidate.Key, candidate.Value);
    }

    void AddExplicitFile(string path, Dictionary<string, string> candidates)
    {
        if (!IsCSharp(path))
            throw new FileSelectionException($"Unsupported file path: {Display(path)}");
        candidates.TryAdd(Path.GetFullPath(path), Display(path));
    }

    void AddDirectory(string root, Dictionary<string, string> candidates, CancellationToken cancellationToken)
    {
        var rules = new GitIgnoreRules();
        Visit(root, root, rules, candidates, cancellationToken);
    }

    void Visit(
        string directory,
        string root,
        GitIgnoreRules rules,
        Dictionary<string, string> candidates,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        rules.AddFile(directory, root);
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(directory).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            throw new FileSelectionException($"Path is inaccessible: {Display(directory)}");
        }

        foreach (var entry in entries)
        {
            var attributes = File.GetAttributes(entry);
            var isDirectory = attributes.HasFlag(FileAttributes.Directory);
            var relativePath = Path.GetRelativePath(root, entry).Replace('\\', '/');
            if (isDirectory)
            {
                if (VcsDirectories.Contains(Path.GetFileName(entry)) || attributes.HasFlag(FileAttributes.ReparsePoint))
                    continue;
                if (rules.IsIgnored(relativePath, directory: true))
                    continue;
                Visit(entry, root, rules, candidates, cancellationToken);
            }
            else if (IsCSharp(entry) && !rules.IsIgnored(relativePath, directory: false))
            {
                var fullPath = Path.GetFullPath(entry);
                candidates.TryAdd(fullPath, Display(fullPath));
            }
        }
    }

    string Display(string path)
    {
        var relative = Path.GetRelativePath(_invocationDirectory, path);
        var display = relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || relative == ".."
            ? Path.GetFullPath(path)
            : relative;
        return display.Replace('\\', '/');
    }

    static void RejectUnsupportedInclude(string include)
    {
        if (string.IsNullOrWhiteSpace(include))
            throw new FileSelectionException("Include cannot be empty.");
        if (include == "-")
            throw new FileSelectionException("Standard input is not supported.");
        if (IsGlob(include) && Path.IsPathRooted(include))
            throw new FileSelectionException($"Include globs must be relative to the invocation directory: {include}");
    }

    static bool IsGlob(string include) => include.Contains('*', StringComparison.Ordinal);

    static string NormalizeGlob(string include)
    {
        var pattern = include.Replace('\\', '/');
        if (pattern.Split('/').Contains("..", StringComparer.Ordinal))
            throw new FileSelectionException($"Include globs cannot leave the invocation directory: {include}");
        return pattern.StartsWith("./", StringComparison.Ordinal) ? pattern[2..] : pattern;
    }

    static bool IsCSharp(string path) => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

    static async Task<bool> IsGeneratedAsync(string path, CancellationToken cancellationToken)
    {
        if (GeneratedSuffixes.Any(suffix => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            return true;

        using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
        for (var count = 0; count < 20 && await reader.ReadLineAsync(cancellationToken) is { } line; count++)
        {
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0)
                continue;
            return trimmed.StartsWith("//", StringComparison.Ordinal)
                && trimmed.Contains("<auto-generated", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    static StringComparer PathIdentityComparer() => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    static StringComparison PathComparison() => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
