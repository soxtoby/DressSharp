#!/usr/bin/env dotnet
#:package DotNetDo.Core@0.8.0
using DotNetDo;

[assembly: TaskDescription("Materialize and verify the pinned 1,000-file benchmark corpus, optionally updating its lock.")]

var update = Do.Param("update", false, "Update the corpus lock manifest.").Value;
var benchmarkRoot = Do.RootDirectory / ".benchmarks";
var definition = (benchmarkRoot / "corpus/sources.json").ReadJson()!["sources"]!.AsArray();
var corpus = benchmarkRoot / "corpus/files";
var scratch = Do.CreateTempDirectory("DressSharp-corpus-");

try
{
    corpus.RecreateDirectory();
    (benchmarkRoot / "corpus/Corpus.csproj").CopyTo(corpus / "Corpus.csproj");
    (Do.RootDirectory / "tests/DressSharp.UnitTests/Fixtures/Default.editorconfig").CopyTo(corpus / ".editorconfig");

    foreach (var sourceNode in definition)
    {
        var source = sourceNode!.AsObject();
        var name = source["name"]!.GetValue<string>();
        var url = source["url"]!.GetValue<string>();
        var revision = source["revision"]!.GetValue<string>();
        var count = source["count"]!.GetValue<int>();
        var checkout = scratch / name;
        await Do.Exec($"git clone --quiet --filter=blob:none --no-checkout {url.QuotedArgument()} {checkout.QuotedArgument()}");
        await Do.Exec($"git -C {checkout.QuotedArgument()} -c core.longpaths=true checkout --quiet {revision.QuotedArgument()}");
        var files = checkout.GlobFiles(["**/*.cs", "!**/.git/**", "!**/bin/**", "!**/obj/**", "!**/Generated/**", "!**/*.g.cs", "!**/*.generated.cs"])
            .OrderBy(path => checkout.RelativePathTo(path).UnixPath, StringComparer.Ordinal)
            .Take(count)
            .ToArray();
        if (files.Length != count)
            throw new InvalidOperationException($"{name} has only {files.Length} eligible files.");
        foreach (var file in files)
        {
            file.CopyTo(corpus / name / checkout.RelativePathTo(file), new() { CreateDirectories = true });
        }
    }

    var command = $"dotnet run --project {(Do.RootDirectory / "benchmarks/CorpusInspector/CorpusInspector.csproj").QuotedArgument()} -- {corpus.QuotedArgument()} {(benchmarkRoot / "corpus/manifest.json").QuotedArgument()}";
    if (update)
        command += " --update";
    await Do.Exec(command);
}
finally
{
    DeleteDirectory(scratch);
}

static void DeleteDirectory(string path)
{
    if (!Directory.Exists(path))
        return;
    foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        File.SetAttributes(file, FileAttributes.Normal);
    Directory.Delete(path, true);
}
