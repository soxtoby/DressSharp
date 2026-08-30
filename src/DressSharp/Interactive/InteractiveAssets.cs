using System.Reflection;

namespace DressSharp.Interactive;

static class InteractiveAssets
{
    internal static byte[] Index { get; } = Read("index.html");
    internal static byte[] JavaScript { get; } = Read("app.js");
    internal static byte[] Css { get; } = Read("app.css");

    static byte[] Read(string name)
    {
        var resourceName = $"DressSharp.Interactive.{name}";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded interactive asset '{name}' is missing. Run 'dotnet do build-interactive'.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
