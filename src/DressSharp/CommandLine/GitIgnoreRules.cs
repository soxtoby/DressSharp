using System.Text.RegularExpressions;

namespace DressSharp.CommandLine;

sealed class GitIgnoreRules
{
    readonly List<Rule> rules = [];

    internal void AddFile(string directory, string selectionRoot)
    {
        var ignoreFile = Path.Combine(directory, ".gitignore");
        if (!File.Exists(ignoreFile))
            return;

        var basePath = Path.GetRelativePath(selectionRoot, directory).Replace('\\', '/');
        if (basePath == ".")
            basePath = string.Empty;

        foreach (var sourceLine in File.ReadLines(ignoreFile))
        {
            var line = sourceLine.TrimEnd();
            if (line.Length == 0 || line[0] == '#')
                continue;

            var negated = line[0] == '!';
            if (negated)
                line = line[1..];
            else if (line.StartsWith("\\!", StringComparison.Ordinal) || line.StartsWith("\\#", StringComparison.Ordinal))
                line = line[1..];

            var directoryOnly = line.EndsWith('/');
            line = line.TrimEnd('/');
            if (line.Length == 0)
                continue;

            rules.Add(new Rule(CreatePattern(basePath, line), negated, directoryOnly));
        }
    }

    internal bool IsIgnored(string relativePath, bool directory)
    {
        var path = relativePath.Replace('\\', '/');
        var ignored = false;
        foreach (var rule in rules)
        {
            if (rule.Pattern.IsMatch(path))
                ignored = !rule.Negated;
        }
        return ignored;
    }

    static Regex CreatePattern(string basePath, string pattern)
    {
        var anchored = pattern.StartsWith('/');
        pattern = pattern.TrimStart('/');
        var hasSlash = pattern.Contains('/');
        var prefix = Regex.Escape(basePath);
        if (prefix.Length > 0)
            prefix += "/";

        var body = Regex.Escape(pattern)
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", "[^/]*")
            .Replace(@"\?", "[^/]");
        var start = anchored || hasSlash
            ? "^" + prefix
            : prefix.Length == 0
                ? @"^(?:.*/)?"
                : "^" + prefix + @"(?:.*/)?";
        return new Regex(start + body + @"(?:/.*)?$", RegexOptions.CultureInvariant);
    }

    sealed record Rule(Regex Pattern, bool Negated, bool DirectoryOnly);
}
