using System.Text.RegularExpressions;

namespace DressSharp.Configuration;

internal static partial class EditorConfigSyntaxValidator
{
    internal static void Validate(string path, string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0 || line[0] is '#' or ';')
                continue;
            if (line.StartsWith('['))
            {
                if (!line.EndsWith(']') || line.Length == 2)
                    throw Error(path, index + 1, "malformed section header");
                continue;
            }

            var separator = line.IndexOfAny(['=', ':']);
            if (separator <= 0 || !KeyPattern().IsMatch(line[..separator].Trim()) || line[(separator + 1)..].Trim().Length == 0)
                throw Error(path, index + 1, "malformed key/value assignment");

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Equals("root", StringComparison.OrdinalIgnoreCase) &&
                !value.Equals("true", StringComparison.OrdinalIgnoreCase) &&
                !value.Equals("false", StringComparison.OrdinalIgnoreCase))
                throw Error(path, index + 1, "root must be true or false");
        }
    }

    private static ConfigurationException Error(string path, int line, string message) => new($"{path}({line}): {message}.");

    [GeneratedRegex("^[A-Za-z0-9_.-]+$")]
    private static partial Regex KeyPattern();
}
