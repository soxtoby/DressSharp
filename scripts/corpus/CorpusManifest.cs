using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp;

/// <summary>The lock that pins the benchmark corpus: a hash, size, and syntax-node count per file.</summary>
static class CorpusManifest
{
    /// <summary>Checks the materialized corpus against its manifest, or rewrites the manifest when <paramref name="update"/> is set.</summary>
    public static void Verify(string corpus, string manifestPath, bool update)
    {
        corpus = Path.GetFullPath(corpus);
        var entries = Directory.EnumerateFiles(corpus, "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(corpus, path).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Order(StringComparer.Ordinal)
            .Select(path =>
                {
                    var bytes = File.ReadAllBytes(path);
                    var text = File.ReadAllText(path);
                    return new Entry(
                    Path.GetRelativePath(corpus, path).Replace('\\', '/'),
                    Convert.ToHexStringLower(SHA256.HashData(bytes)),
                    bytes.Length,
                    text.Length == 0 ? 0 : text.Count(character => character == '\n') + 1,
                    CSharpSyntaxTree.ParseText(text).GetRoot().DescendantNodesAndSelf().Count());
                }).ToArray();
        var corpusHash = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            string.Join('\n', entries.Select(entry => $"{entry.Path}:{entry.Sha256}")))));
        var json = Serialize(corpusHash, entries);

        if (update)
        {
            File.WriteAllText(manifestPath, json);
        }
        else if (!File.Exists(manifestPath) || File.ReadAllText(manifestPath).Replace("\r\n", "\n", StringComparison.Ordinal) != json)
        {
            throw new InvalidOperationException(
                "Materialized corpus does not match corpus/manifest.json. Run with --update after an intentional source-manifest change.");
        }

        if (entries.Length != 1000)
            throw new InvalidOperationException($"Expected 1,000 files; found {entries.Length}.");
    }

    /// <summary>Writes the manifest by hand, since a script is compiled for trimming and reflection-based serialization is unavailable.</summary>
    static string Serialize(string corpusHash, Entry[] entries)
    {
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);
            writer.WriteString("corpusHash", corpusHash);
            writer.WriteNumber("fileCount", entries.Length);
            writer.WriteStartArray("files");
            foreach (var entry in entries)
            {
                writer.WriteStartObject();
                writer.WriteString("path", entry.Path);
                writer.WriteString("sha256", entry.Sha256);
                writer.WriteNumber("bytes", entry.Bytes);
                writer.WriteNumber("lines", entry.Lines);
                writer.WriteNumber("syntaxNodes", entry.SyntaxNodes);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    sealed record Entry(string Path, string Sha256, int Bytes, int Lines, int SyntaxNodes);
}
