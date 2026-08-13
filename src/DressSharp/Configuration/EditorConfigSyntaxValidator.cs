using System.Text.RegularExpressions;

namespace DressSharp.Configuration;

static partial class EditorConfigSyntaxValidator
{
    internal static void Validate(string path, string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line) || CommentPattern().IsMatch(line))
                continue;
            if (SectionPattern().IsMatch(line))
                continue;

            var property = PropertyPattern().Match(line);
            if (!property.Success)
            {
                var message = line.TrimStart().StartsWith('[') ? "malformed section header" : "malformed key/value assignment";
                throw Error(path, index + 1, message);
            }

            var key = property.Groups[1].Value;
            var value = property.Groups[2].Value;
            if (key.Equals("root", StringComparison.OrdinalIgnoreCase)
                && !value.Equals("true", StringComparison.OrdinalIgnoreCase) 
                && !value.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                throw Error(path, index + 1, "root must be true or false");
            }
        }
    }

    static ConfigurationException Error(string path, int line, string message) => new($"{path}({line}): {message}.");

    [GeneratedRegex(@"^\s*[#;]")]
    private static partial Regex CommentPattern();

    [GeneratedRegex(@"^\s*\[(([^#;]|\\#|\\;)+)\]\s*([#;].*)?$")]
    private static partial Regex SectionPattern();

    [GeneratedRegex(@"^\s*([\w.\-_]+)\s*[=:]\s*(.*?)\s*([#;].*)?$")]
    private static partial Regex PropertyPattern();
}
