using System.Text;

namespace DressSharp.Configuration;

static class EditorConfigSyntaxValidator
{
    internal static string DecodeAndValidate(string path, ReadOnlySpan<byte> bytes)
    {
        var preambleLength = bytes.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 : 0;
        if (preambleLength == 0
            && (bytes.StartsWith(new byte[] { 0xFF, 0xFE })
                || bytes.StartsWith(new byte[] { 0xFE, 0xFF })))
            throw new ConfigurationException($"{path}: EditorConfig files must be UTF-8 encoded.");

        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes[preambleLength..]);
        }
        catch (DecoderFallbackException)
        {
            throw new ConfigurationException($"{path}: EditorConfig files must be UTF-8 encoded.");
        }

        Validate(path, text);
        return text;
    }

    internal static void Validate(string path, string text)
    {
        var inSection = false;
        var lineNumber = 1;
        for (var start = 0; start <= text.Length; lineNumber++)
        {
            var end = text.IndexOfAny(['\r', '\n'], start);
            if (end < 0)
                end = text.Length;
            ValidateLine(path, lineNumber, text[start..end], ref inSection);
            if (end == text.Length)
                break;
            if (text[end] == '\r' && (end + 1 == text.Length || text[end + 1] != '\n'))
                throw Error(path, lineNumber, "line separators must be LF or CRLF");
            start = text[end] == '\r' ? end + 2 : end + 1;
        }
    }

    static void ValidateLine(string path, int lineNumber, string source, ref bool inSection)
    {
        var line = source.Trim();
        if (line.Length == 0 || line[0] is '#' or ';')
            return;
        if (line[0] == '[')
        {
            if (line.Length < 2 || line[^1] != ']')
                throw Error(path, lineNumber, "malformed section header");
            inSection = true;
            return;
        }

        var separator = line.IndexOf('=');
        if (separator <= 0)
            throw Error(path, lineNumber, "malformed key/value assignment");
        var key = line[..separator].Trim();
        var value = line[(separator + 1)..].Trim();
        if (key.Length == 0 || key.Length > 1024 || value.Length > 4096)
            throw Error(path, lineNumber, "invalid key/value assignment");
        if (!key.Equals("root", StringComparison.OrdinalIgnoreCase))
            return;
        if (inSection)
            throw Error(path, lineNumber, "root must appear before the first section");
        if (!value.Equals("true", StringComparison.OrdinalIgnoreCase)
            && !value.Equals("false", StringComparison.OrdinalIgnoreCase))
            throw Error(path, lineNumber, "root must be true or false");
    }

    static ConfigurationException Error(string path, int line, string message) => new($"{path}({line}): {message}.");
}
