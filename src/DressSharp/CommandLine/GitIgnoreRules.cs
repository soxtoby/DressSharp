using GitignoreParserNet;

namespace DressSharp.CommandLine;

sealed class GitIgnoreRules
{
    readonly List<RuleSet> _ruleSets = [];

    internal void AddFile(string directory, string selectionRoot)
    {
        var ignoreFile = Path.Combine(directory, ".gitignore");
        if (!File.Exists(ignoreFile))
            return;

        var basePath = Path.GetRelativePath(selectionRoot, directory).Replace('\\', '/');
        if (basePath == ".")
            basePath = string.Empty;

        _ruleSets.Add(new RuleSet(basePath, new GitignoreParser(File.ReadAllText(ignoreFile), false)));
    }

    internal bool IsIgnored(string relativePath, bool directory)
    {
        var path = relativePath.Replace('\\', '/');
        var ignored = false;
        foreach (var ruleSet in _ruleSets)
        {
            if (!TryGetLocalPath(path, ruleSet.BasePath, out var localPath))
                continue;

            if (directory)
                localPath += "/";
            if (ruleSet.Parser.Inspects(localPath))
                ignored = ruleSet.Parser.Denies(localPath);
        }
        return ignored;
    }

    static bool TryGetLocalPath(string path, string basePath, out string localPath)
    {
        if (basePath.Length == 0)
        {
            localPath = path;
            return true;
        }

        var prefix = basePath + "/";
        if (path.StartsWith(prefix, StringComparison.Ordinal))
        {
            localPath = path[prefix.Length..];
            return true;
        }

        localPath = string.Empty;
        return false;
    }

    sealed record RuleSet(string BasePath, GitignoreParser Parser);
}
