using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp;

var corpus = Path.GetFullPath(args[0]);
var manifestPath = Path.GetFullPath(args[1]);
var entries = Directory.EnumerateFiles(corpus, "*.cs", SearchOption.AllDirectories)
    .Where(path => !Path.GetRelativePath(corpus, path).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
    .Order(StringComparer.Ordinal)
    .Select(path =>
        {
            var bytes = File.ReadAllBytes(path);
            var text = File.ReadAllText(path);
            return new
                {
                    path = Path.GetRelativePath(corpus, path).Replace('\\', '/'),
                    sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
                    bytes = bytes.Length,
                    lines = text.Length == 0 ? 0 : text.Count(character => character == '\n') + 1,
                    syntaxNodes = CSharpSyntaxTree.ParseText(text).GetRoot().DescendantNodesAndSelf().Count()
                };
        }).ToArray();
var corpusHash = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
    string.Join('\n', entries.Select(entry => $"{entry.path}:{entry.sha256}")))));
var json = JsonSerializer.Serialize(
    new { version = 1, corpusHash, fileCount = entries.Length, files = entries },
    new JsonSerializerOptions { WriteIndented = true })
    .Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";

if (args.Contains("--update", StringComparer.Ordinal))
{
    File.WriteAllText(manifestPath, json);
}
else if (!File.Exists(manifestPath) || File.ReadAllText(manifestPath).Replace("\r\n", "\n", StringComparison.Ordinal) != json)
{
    throw new InvalidOperationException(
        "Materialized corpus does not match corpus/manifest.json. Run with -UpdateLock after an intentional source-manifest change.");
}

if (entries.Length != 1000)
    throw new InvalidOperationException($"Expected 1,000 files; found {entries.Length}.");
