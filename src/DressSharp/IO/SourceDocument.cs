using System.Text;

namespace DressSharp.IO;

sealed class SourceDocument
{
    readonly byte[] _originalBytes;

    SourceDocument(string path, byte[] bytes, string text, SourceEncoding sourceEncoding, string preferredLineEnding)
    {
        Path = path;
        _originalBytes = bytes;
        Text = text;
        SourceEncoding = sourceEncoding;
        PreferredLineEnding = preferredLineEnding;
    }

    internal string Path { get; }
    internal string Text { get; }
    internal SourceEncoding SourceEncoding { get; }
    internal string PreferredLineEnding { get; }
    internal ReadOnlyMemory<byte> OriginalBytes => _originalBytes;

    internal static async ValueTask<SourceDocument> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var (encoding, preambleLength, decoder) = DetectEncoding(bytes);
        string text;
        try
        {
            text = decoder.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        }
        catch (DecoderFallbackException exception)
        {
            throw new SourceIOException($"'{path}' is not valid {DisplayName(encoding)} text.", exception);
        }
        return new SourceDocument(path, bytes, text, encoding, FirstLineEnding(text) ?? Environment.NewLine);
    }

    internal byte[] Encode(string transformedText, RepresentationPreferences preferences)
    {
        var text = transformedText;
        if (preferences.TrimTrailingWhitespace is true)
            text = TrimTrailingWhitespace(text);
        if (preferences.EndOfLine is not null)
            text = ReplaceLineEndings(text, preferences.EndOfLine);
        if (preferences.InsertFinalNewline is true && !EndsWithLineEnding(text))
            text += preferences.EndOfLine ?? PreferredLineEnding;
        else if (preferences.InsertFinalNewline is false)
            text = TrimFinalLineEndings(text);

        var encoding = preferences.Encoding ?? SourceEncoding;
        if (text == Text && encoding == SourceEncoding)
            return _originalBytes.ToArray();
        var encoder = CreateEncoder(encoding);
        var content = encoder.GetBytes(text);
        var preamble = encoder.GetPreamble();
        if (preamble.Length == 0)
            return content;
        var result = new byte[preamble.Length + content.Length];
        preamble.CopyTo(result, 0);
        content.CopyTo(result, preamble.Length);
        return result;
    }

    static (SourceEncoding Encoding, int PreambleLength, Encoding Decoder) DetectEncoding(byte[] bytes)
    {
        if (StartsWith(bytes, [0x00, 0x00, 0xFE, 0xFF]) || StartsWith(bytes, [0xFF, 0xFE, 0x00, 0x00]))
            throw new SourceIOException("UTF-32 source files are not supported.");
        if (StartsWith(bytes, [0xEF, 0xBB, 0xBF]))
            return (SourceEncoding.Utf8Bom, 3, new UTF8Encoding(false, true));
        if (StartsWith(bytes, [0xFF, 0xFE]))
            return (SourceEncoding.Utf16LittleEndian, 2, new UnicodeEncoding(false, false, true));
        if (StartsWith(bytes, [0xFE, 0xFF]))
            return (SourceEncoding.Utf16BigEndian, 2, new UnicodeEncoding(true, false, true));
        var utf8 = new UTF8Encoding(false, true);
        try
        {
            _ = utf8.GetString(bytes);
            return (SourceEncoding.Utf8, 0, utf8);
        }
        catch (DecoderFallbackException)
        {
            return (SourceEncoding.Latin1, 0, Latin1());
        }
    }

    static Encoding CreateEncoder(SourceEncoding encoding) => encoding switch
    {
        SourceEncoding.Utf8 => new UTF8Encoding(false, true),
        SourceEncoding.Utf8Bom => new UTF8Encoding(true, true),
        SourceEncoding.Utf16LittleEndian => new UnicodeEncoding(false, true, true),
        SourceEncoding.Utf16BigEndian => new UnicodeEncoding(true, true, true),
        SourceEncoding.Latin1 => Latin1(),
        _ => throw new ArgumentOutOfRangeException(nameof(encoding))
    };

    static Encoding Latin1() => Encoding.GetEncoding(
        28591,
        EncoderFallback.ExceptionFallback,
        DecoderFallback.ExceptionFallback);

    static bool StartsWith(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> prefix) => bytes.StartsWith(prefix);

    static string? FirstLineEnding(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            switch (text[index])
            {
                case '\r':
                    return index + 1 < text.Length && text[index + 1] == '\n' ? "\r\n" : "\r";
                case '\n':
                    return "\n";
            }
        }
        return null;
    }

    static string ReplaceLineEndings(string text, string lineEnding)
    {
        var builder = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            switch (text[index])
            {
                case '\r':
                    if (index + 1 < text.Length && text[index + 1] == '\n')
                        index++;
                    builder.Append(lineEnding);
                    break;
                
                case '\n':
                    builder.Append(lineEnding);
                    break;
                
                default:
                    builder.Append(text[index]);
                    break;
            }
        }
        return builder.ToString();
    }

    static string TrimTrailingWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var lineStart = 0;
        for (var index = 0; index <= text.Length; index++)
        {
            if (index < text.Length && text[index] is not ('\r' or '\n'))
                continue;
            var end = index;
            while (end > lineStart && text[end - 1] is ' ' or '\t')
                end--;
            builder.Append(text, lineStart, end - lineStart);
            if (index < text.Length)
            {
                builder.Append(text[index]);
                if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                    builder.Append(text[++index]);
            }
            lineStart = index + 1;
        }
        return builder.ToString();
    }

    static bool EndsWithLineEnding(string text) => text.EndsWith('\r') || text.EndsWith('\n');

    static string TrimFinalLineEndings(string text)
    {
        var end = text.Length;
        while (end > 0 && text[end - 1] is '\r' or '\n')
            end--;
        return text[..end];
    }

    static string DisplayName(SourceEncoding encoding) => encoding switch
    {
        SourceEncoding.Utf8Bom => "UTF-8",
        SourceEncoding.Utf16LittleEndian => "UTF-16 LE",
        SourceEncoding.Utf16BigEndian => "UTF-16 BE",
        _ => encoding.ToString()
    };
}

enum SourceEncoding
{
    Utf8,
    Utf8Bom,
    Utf16LittleEndian,
    Utf16BigEndian,
    Latin1
}

sealed record RepresentationPreferences(
    SourceEncoding? Encoding = null,
    string? EndOfLine = null,
    bool? InsertFinalNewline = null,
    bool? TrimTrailingWhitespace = null);

sealed class SourceIOException(string message, Exception? innerException = null) : IOException(message, innerException);
