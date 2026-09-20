using System.Globalization;
using System.IO.Compression;
using System.Reflection;

namespace DressSharp.Interactive;

static class InteractiveAssets
{
    internal static byte[] Index { get; } = Read("index.html");
    internal static InteractiveAsset JavaScript { get; } = ReadJavaScript();
    internal static byte[] Css { get; } = Read("app.css");
    internal static byte[] Icon { get; } = Read("dresssharp.svg");
    internal static byte[] FaviconSvg { get; } = Read("favicon.svg");
    internal static byte[] FaviconIco { get; } = Read("favicon.ico");

    static InteractiveAsset ReadJavaScript()
    {
        var compressed = TryRead("app.js.gz");
        return compressed is null
            ? new(Read("app.js"), false)
            : new(compressed, true);
    }

    static byte[] Read(string name)
    {
        using var stream = Open(name)
            ?? throw new InvalidOperationException($"Embedded interactive asset '{name}' is missing. Run 'dotnet do build-interactive'.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    static byte[]? TryRead(string name)
    {
        using var stream = Open(name);
        if (stream is null)
            return null;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    static Stream? Open(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream($"DressSharp.Interactive.{name}");
}

sealed class InteractiveAsset(byte[] content, bool gzipCompressed)
{
    internal (byte[] Content, string? ContentEncoding) ForRequest(string? acceptEncoding)
    {
        var acceptsGzip = AcceptsGzip(acceptEncoding);
        if (!gzipCompressed || acceptsGzip)
            return (content, gzipCompressed ? "gzip" : null);

        using var compressed = new MemoryStream(content);
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var plain = new MemoryStream();
        gzip.CopyTo(plain);
        return (plain.ToArray(), null);
    }

    static bool AcceptsGzip(string? acceptEncoding)
    {
        if (acceptEncoding is null)
            return false;

        foreach (var value in acceptEncoding.Split(','))
        {
            var parts = value.Split(';', StringSplitOptions.TrimEntries);
            if (!parts[0].Equals("gzip", StringComparison.OrdinalIgnoreCase))
                continue;

            var quality = parts.Skip(1).FirstOrDefault(part => part.StartsWith("q=", StringComparison.OrdinalIgnoreCase));
            return quality is null
                || double.TryParse(quality.AsSpan(2), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
                    && parsed > 0;
        }

        return false;
    }
}
