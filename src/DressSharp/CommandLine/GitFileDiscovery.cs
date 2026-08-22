using System.Diagnostics;
using System.Text;

namespace DressSharp.CommandLine;

sealed class GitFileDiscovery
{
    internal async ValueTask<IReadOnlyList<string>?> TryListAsync(string directory, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git")
            {
                ArgumentList = { "-C", directory, "ls-files", "--cached", "--others", "--exclude-standard", "-z" },
                StandardOutputEncoding = Encoding.UTF8,
                Environment =
                    {
                        ["LANG"] = "C",
                        ["LC_ALL"] = "C",
                    }
            };
        var result = await ProcessRunner.TryRunAsync(startInfo, cancellationToken);
        if (result is null)
            return null;

        if (result.ExitCode != 0)
            return result.StandardError.Contains("not a git repository", StringComparison.OrdinalIgnoreCase) 
                ? null 
                : throw new FileSelectionException($"Git file discovery failed: {result.StandardError.Trim()}");

        return result.StandardOutput
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Replace('\\', '/'))
            .ToArray();
    }
}
