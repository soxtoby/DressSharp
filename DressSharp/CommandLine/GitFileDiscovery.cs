using System.Diagnostics;
using System.Text;

namespace DressSharp.CommandLine;

sealed class GitFileDiscovery
{
    internal ValueTask<IReadOnlyList<string>?> TryListAsync(string directory, CancellationToken cancellationToken) =>
        TryRunAsync(directory, ["ls-files", "--cached", "--others", "--exclude-standard", "-z"], cancellationToken);

    /// <summary>
    /// Lists the files beneath <paramref name="directory"/> that Git reports as changed, or
    /// <see langword="null"/> outside a Git worktree. Deleted files are never listed.
    /// </summary>
    internal async ValueTask<IReadOnlyList<string>?> TryListChangedAsync(string directory, SelectionScope scope, CancellationToken cancellationToken)
    {
        // Plumbing rather than `git diff`, which outside a repository compares paths instead of
        // reporting that there is no repository. A repository without a commit has no HEAD to
        // diff against, so every tracked file is new.
        string[] arguments = scope == SelectionScope.Staged
            ? ["diff-index", "--cached", "--name-only", "--relative", "--diff-filter=d", "-z", "HEAD"]
            : ["diff-index", "--name-only", "--relative", "--diff-filter=d", "-z", "HEAD"];
        var modified = await TryRunAsync(directory, arguments, cancellationToken, tolerateMissingHead: true)
            ?? await TryRunAsync(directory, ["ls-files", "--cached", "-z"], cancellationToken);
        if (modified is null || scope == SelectionScope.Staged)
            return modified;
        var untracked = await TryRunAsync(directory, ["ls-files", "--others", "--exclude-standard", "-z"], cancellationToken) ?? [];
        return [.. modified, .. untracked];
    }

    /// <summary>
    /// Stages <paramref name="paths"/> so that a pre-commit hook needs no second command to
    /// carry a rewrite into the commit.
    /// </summary>
    internal async ValueTask StageAsync(string directory, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        if (paths.Count == 0)
            return;
        await TryRunAsync(directory, ["add", "--", .. paths], cancellationToken);
    }

    static async ValueTask<IReadOnlyList<string>?> TryRunAsync(
        string directory,
        string[] arguments,
        CancellationToken cancellationToken,
        bool tolerateMissingHead = false)
    {
        var startInfo = new ProcessStartInfo("git")
            {
                ArgumentList = { "-C", directory },
                StandardOutputEncoding = Encoding.UTF8,
                Environment =
                    {
                        ["LANG"] = "C",
                        ["LC_ALL"] = "C",
                    }
            };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        var result = await ProcessRunner.TryRunAsync(startInfo, cancellationToken);
        if (result is null)
            return null;

        if (result.ExitCode != 0)
        {
            if (result.StandardError.Contains("not a git repository", StringComparison.OrdinalIgnoreCase))
                return null;
            if (tolerateMissingHead && result.StandardError.Contains("HEAD", StringComparison.Ordinal))
                return null;
            throw new FileSelectionException($"Git file discovery failed: {result.StandardError.Trim()}");
        }

        return result.StandardOutput
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Replace('\\', '/'))
            .ToArray();
    }
}
