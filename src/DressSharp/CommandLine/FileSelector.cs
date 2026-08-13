namespace DressSharp.CommandLine;

sealed class FileSelector(string invocationDirectory)
{
    static readonly HashSet<string> VcsDirectories = new(StringComparer.OrdinalIgnoreCase) { ".git", ".hg", ".svn" };
    static readonly string[] GeneratedSuffixes = [".g.cs", ".generated.cs", ".designer.cs"];
    readonly string invocationDirectory = Path.GetFullPath(invocationDirectory);

    internal async Task<IReadOnlyList<SelectedFile>> SelectAsync(
        IReadOnlyList<string> operands,
        CancellationToken cancellationToken = default)
    {
        var candidates = new Dictionary<string, string>(PathIdentityComparer());
        foreach (var operand in operands.Count == 0 ? [invocationDirectory] : operands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectUnsupportedOperand(operand);
            var fullPath = Path.GetFullPath(operand, invocationDirectory);
            if (File.Exists(fullPath))
                await AddExplicitFileAsync(fullPath, candidates, cancellationToken);
            else if (Directory.Exists(fullPath))
                await AddDirectoryAsync(fullPath, candidates, cancellationToken);
            else
                throw new FileSelectionException($"Path does not exist or is inaccessible: {Display(fullPath)}");
        }

        return candidates
            .Select(pair => new SelectedFile(pair.Key, pair.Value))
            .OrderBy(file => file.DisplayPath, StringComparer.Ordinal)
            .ToArray();
    }

    async Task AddExplicitFileAsync(
        string path,
        Dictionary<string, string> candidates,
        CancellationToken cancellationToken)
    {
        if (!IsCSharp(path))
            throw new FileSelectionException($"Unsupported file path: {Display(path)}");
        if (!await IsGeneratedAsync(path, cancellationToken))
            candidates.TryAdd(Path.GetFullPath(path), Display(path));
    }

    async Task AddDirectoryAsync(
        string root,
        Dictionary<string, string> candidates,
        CancellationToken cancellationToken)
    {
        var rules = new GitIgnoreRules();
        await VisitAsync(root, root, rules, candidates, cancellationToken);
    }

    async Task VisitAsync(
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
                await VisitAsync(entry, root, rules, candidates, cancellationToken);
            }
            else if (IsCSharp(entry) && !rules.IsIgnored(relativePath, false) && !await IsGeneratedAsync(entry, cancellationToken))
            {
                var fullPath = Path.GetFullPath(entry);
                candidates.TryAdd(fullPath, Display(fullPath));
            }
        }
    }

    string Display(string path)
    {
        var relative = Path.GetRelativePath(invocationDirectory, path);
        var display = relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || relative == ".."
            ? Path.GetFullPath(path)
            : relative;
        return display.Replace('\\', '/');
    }

    static void RejectUnsupportedOperand(string operand)
    {
        if (operand == "-")
            throw new FileSelectionException("Standard input is not supported.");
        if (operand.IndexOfAny(['*', '?']) >= 0)
            throw new FileSelectionException($"Globs are not supported: {operand}");
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
}
