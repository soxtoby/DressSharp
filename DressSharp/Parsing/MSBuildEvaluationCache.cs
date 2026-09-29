using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DressSharp.Parsing;

/// <summary>
/// Remembers MSBuild evaluations between runs, keyed by the files that can change the answer.
/// </summary>
/// <remarks>
/// Evaluating a project means starting a <c>dotnet</c> process and waiting roughly a second, which
/// on a repository of any size costs more than reading, parsing and formatting every file put
/// together. The inputs almost never change between runs, so the evaluation is recorded against a
/// fingerprint of them and reused until one of them is touched.
/// </remarks>
sealed class MSBuildEvaluationCache(IMSBuildEvaluator inner) : IMSBuildEvaluator
{
    const string DisableEnvironmentVariable = "DRESSSHARP_NO_MSBUILD_CACHE";
    const char Separator = '|';

    /// <summary>
    /// Files anywhere above the project that MSBuild imports implicitly, plus the restore outputs
    /// beside it. A change to any of them can change the evaluated properties or compile items.
    /// </summary>
    static readonly string[] DirectoryScopedInputs =
        [
            "Directory.Build.props",
            "Directory.Build.targets",
            "Directory.Packages.props",
            "global.json",
            "nuget.config",
            "NuGet.config",
        ];

    static bool Enabled => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DisableEnvironmentVariable));

    public async ValueTask<MSBuildEvaluation> EvaluateAsync(
        string target,
        string configuration,
        string? targetFramework,
        MSBuildEvaluationKind kind,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
            return await inner.EvaluateAsync(target, configuration, targetFramework, kind, cancellationToken);

        var path = EntryPath(target, configuration, targetFramework, kind);
        if (Read(path) is { } cached)
            return cached;

        var evaluation = await inner.EvaluateAsync(target, configuration, targetFramework, kind, cancellationToken);

        // A failed evaluation usually means something the user is about to fix, so it is not worth
        // remembering, and remembering it would hide the fix until an input file happened to change.
        if (evaluation.Succeeded)
            Write(path, evaluation);
        return evaluation;
    }

    static MSBuildEvaluation? Read(string? path)
    {
        if (path is null || !File.Exists(path))
            return null;

        try
        {
            var entry = JsonSerializer.Deserialize<Entry>(File.ReadAllBytes(path));
            return entry is null
                ? null
                : new MSBuildEvaluation(true, entry.Properties, entry.CompileItems, "");
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static void Write(string? path, MSBuildEvaluation evaluation)
    {
        if (path is null)
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var entry = new Entry(
                evaluation.Properties.ToDictionary(pair => pair.Key, pair => pair.Value),
                [.. evaluation.CompileItems]);

            // Concurrent runs may race on the same entry, so publish it by rename rather than
            // letting a reader see a half-written file.
            var staging = $"{path}.{Environment.ProcessId}.tmp";
            File.WriteAllBytes(staging, JsonSerializer.SerializeToUtf8Bytes(entry));
            File.Move(staging, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A cache that cannot be written is only a missed optimisation.
        }
    }

    static string? EntryPath(string target, string configuration, string? targetFramework, MSBuildEvaluationKind kind)
    {
        try
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return string.IsNullOrEmpty(root)
                ? null
                : Path.Combine(root, "DressSharp", "msbuild", $"{Fingerprint(target, configuration, targetFramework, kind)}.json");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static string Fingerprint(string target, string configuration, string? targetFramework, MSBuildEvaluationKind kind)
    {
        var builder = new StringBuilder();
        builder.Append(typeof(MSBuildEvaluationCache).Assembly.GetName().Version)
            .Append(Separator).Append(target)
            .Append(Separator).Append(configuration)
            .Append(Separator).Append(targetFramework)
            .Append(Separator).Append(kind);

        foreach (var input in Inputs(target))
        {
            var info = new FileInfo(input);
            builder.Append(Separator).Append(input)
                .Append(Separator).Append(info.Exists ? info.LastWriteTimeUtc.Ticks : -1)
                .Append(Separator).Append(info.Exists ? info.Length : -1);
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    static IEnumerable<string> Inputs(string target)
    {
        yield return target;

        var directory = Path.GetDirectoryName(Path.GetFullPath(target));
        var projectDirectory = directory;
        while (directory is not null)
        {
            foreach (var name in DirectoryScopedInputs)
                yield return Path.Combine(directory, name);
            directory = Path.GetDirectoryName(directory);
        }

        if (projectDirectory is null)
            yield break;

        // Restore writes the package graph into obj, and that graph feeds evaluated properties.
        var objDirectory = Path.Combine(projectDirectory, "obj");
        if (!Directory.Exists(objDirectory))
            yield break;
        foreach (var generated in Directory.EnumerateFiles(objDirectory, "*.nuget.g.*").Order(StringComparer.Ordinal))
            yield return generated;
        yield return Path.Combine(objDirectory, "project.assets.json");
    }

    sealed record Entry(Dictionary<string, string> Properties, string[] CompileItems);
}
