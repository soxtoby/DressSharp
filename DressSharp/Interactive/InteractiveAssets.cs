using System.Globalization;
using System.IO.Compression;
using System.Reflection;

namespace DressSharp.Interactive;

static class InteractiveAssets
{
    internal static byte[] Index => field ??= Read("index.html");
    internal static InteractiveAsset JavaScript => field ??= ReadJavaScript();
    internal static byte[] Css => field ??= Read("app.css");
    internal static byte[] Icon => field ??= Read("dresssharp.svg");
    internal static byte[] FaviconSvg => field ??= Read("favicon.svg");
    internal static byte[] FaviconIco => field ??= Read("favicon.ico");

    /// <summary>A checkout builds without the browser assets; only the interactive command needs them.</summary>
    internal static void EnsureBuilt()
    {
        using var index = Open("index.html");
        if (index is null)
            throw new InvalidOperationException(MissingMessage);
    }

    const string MissingMessage = "The interactive browser assets were not built into this DressSharp. Run 'dotnet do build-interactive', then build again.";

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
            ?? throw new InvalidOperationException(MissingMessage);
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
