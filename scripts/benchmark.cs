#!/usr/bin/env dotnet
#:package DotNetDo.Core@0.8.0
using DotNetDo;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using static DotNetDo.Tools;

[assembly: TaskDescription("Compare DressSharp with pinned dotnet format using the versioned benchmark protocol.")]

var mode = Do.Param("mode", "check", "Benchmark mode: check or format.").Value;
var powerMode = Do.Param("power-mode", "unknown", "Recorded machine power mode.").Value;
var caveats = Do.Param("background-load-caveats", "none observed", "Recorded background-load caveats.").Value;
var warmups = Do.Param("warmups", 3, "Untimed warmup pairs.").Value;
var measurements = Do.Param("measurements", 15, "Measured alternating pairs.").Value;
var fileCount = Do.Param("file-count", 1000, "Corpus files to use; lower values are smoke-only.").Value;
var workerProfiles = Do.Param("worker-profiles").Value?
    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
    .Select(int.Parse)
    .ToArray();

if (mode is not ("check" or "format"))
    throw new ArgumentException("Mode must be check or format.");

if (fileCount is < 1 or > 1000)
    throw new ArgumentOutOfRangeException(nameof(fileCount));

var benchmarkRoot = Do.RootDirectory / ".benchmarks";
var corpus = benchmarkRoot / "corpus" / "files";
var resultsDirectory = (benchmarkRoot / "results").EnsureDirectoryExists();
var timestamp = DateTimeOffset.UtcNow;

await (Bun.Run with { Target = "build", WorkingDirectory = Do.RootDirectory / "src" / "DressSharp" / "InteractiveWeb" });
await (DotNet.Build with
    {
        Targets = [Do.RootDirectory / "src/DressSharp/DressSharp.csproj"],
        Configuration = "Release",
        NoLogo = true
    });

if (!corpus.IsExistingDirectory)
{
    await Do.Exec("dotnet do materialize-corpus");
}
else
{
    (benchmarkRoot / "corpus/Corpus.csproj").CopyTo(corpus / "Corpus.csproj", new() { Overwrite = true });
    var inspector = Do.RootDirectory / "benchmarks/CorpusInspector/CorpusInspector.csproj";
    var manifest = benchmarkRoot / "corpus/manifest.json";
    await Do.Exec($"dotnet run --project {inspector.QuotedArgument()} -- {corpus.QuotedArgument()} {manifest.QuotedArgument()}");
}

var benchmarkCorpus = Do.CreateTempDirectory("DressSharp-corpus-");
try
{
    var benchmarkFiles = (benchmarkCorpus / "files").EnsureDirectoryExists();
    (corpus / "Corpus.csproj").CopyTo(benchmarkCorpus / "Corpus.csproj");
    (corpus / ".editorconfig").CopyTo(benchmarkCorpus / ".editorconfig");
    (Do.RootDirectory / ".gitignore").CopyTo(benchmarkCorpus / ".gitignore");

    var files = corpus.GlobFiles("**/*.cs").Take(fileCount);
    var index = 0;
    foreach (var file in files)
        file.CopyTo(benchmarkFiles / $"{index++:D4}-{file.Name}");

    await Do.Exec($"git -C {benchmarkCorpus.QuotedArgument()} init --quiet");
    await (DotNet.Restore with
        {
            Targets = [benchmarkCorpus / "Corpus.csproj"]
        });

    var logicalCores = Environment.ProcessorCount;
    workerProfiles ??= [Math.Min(Math.Max(logicalCores / 2, 1), 16), 1, logicalCores];
    var profiles = new JsonArray();
    foreach (var workers in workerProfiles.Distinct())
    {
        for (var warmup = 0; warmup < warmups; warmup++)
        {
            await Measure("DressSharp", workers, false);
            await Measure("dotnet format", workers, false);
        }

        var runs = new List<Measurement>();
        for (var iteration = 0; iteration < measurements; iteration++)
        {
            var order = iteration % 2 == 0 ? new[] { "DressSharp", "dotnet format" } : ["dotnet format", "DressSharp"];
            foreach (var tool in order)
                runs.Add((await Measure(tool, workers, true))!);
        }

        var dress = Summary(runs, "DressSharp");
        var format = Summary(runs, "dotnet format");
        var runNodes = new JsonArray(runs.Select(MeasurementNode).ToArray());
        profiles.Add((JsonNode)new JsonObject
            {
                ["workers"] = workers,
                ["runs"] = runNodes,
                ["summary"] = new JsonObject
                    {
                        ["dressSharp"] = SummaryNode(dress),
                        ["dotnetFormat"] = SummaryNode(format),
                        ["medianRatio"] = dress.MedianMilliseconds / format.MedianMilliseconds
                    }
            });
    }

    var manifest = (benchmarkRoot / "corpus/manifest.json").ReadJson()!.AsObject();
    var dotnetVersion = await Capture("dotnet --version");
    var result = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["timestamp"] = timestamp.ToString("O"),
            ["mode"] = mode,
            ["environment"] = new JsonObject
                {
                    ["cpu"] = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? RuntimeInformation.ProcessArchitecture.ToString(),
                    ["logicalCores"] = logicalCores,
                    ["ramBytes"] = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
                    ["os"] = RuntimeInformation.OSDescription,
                    ["powerMode"] = powerMode,
                    ["dotnetSdk"] = dotnetVersion,
                    ["gitCommit"] = Do.GitRepo.CurrentCommit.Sha,
                    ["backgroundLoadCaveats"] = caveats
                },
            ["corpus"] = new JsonObject { ["version"] = manifest["version"]!.DeepClone(), ["hash"] = manifest["corpusHash"]!.DeepClone(), ["fileCount"] = fileCount },
            ["tools"] = new JsonObject { ["dressSharp"] = "0.1.0", ["dotnetFormat"] = "SDK " + dotnetVersion, ["roslyn"] = "5.6.0" },
            ["profiles"] = profiles
        };
    var stem = $"{timestamp:yyyyMMddTHHmmssZ}-{mode}";
    var jsonPath = resultsDirectory / (stem + ".json");
    jsonPath.WriteText(result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    var markdown = new List<string> { $"# Benchmark {timestamp:u}", "", $"Mode: {mode}  ", $"Corpus: {manifest["corpusHash"]}", "", "| Workers | DressSharp median | DressSharp p95 | dotnet format median | Ratio |", "| ---: | ---: | ---: | ---: | ---: |" };
    foreach (var profile in profiles.Cast<JsonObject>())
    {
        var summary = profile["summary"]!.AsObject();
        markdown.Add($"| {profile["workers"]} | {summary["dressSharp"]!["medianMilliseconds"]} ms | {summary["dressSharp"]!["p95Milliseconds"]} ms | {summary["dotnetFormat"]!["medianMilliseconds"]} ms | {summary["medianRatio"]} |");
    }

    (resultsDirectory / (stem + ".md")).WriteLines(markdown);
    Console.WriteLine(jsonPath);

    async Task<Measurement?> Measure(string tool, int workers, bool measured)
    {
        var runRoot = benchmarkCorpus;
        AbsolutePath? scratch = null;
        if (mode == "format")
        {
            scratch = Do.CreateTempDirectory("DressSharp-run-");
            benchmarkCorpus.CopyTo(scratch, new() { Overwrite = true });
            runRoot = scratch;
            await Do.Exec($"git -C {runRoot.QuotedArgument()} init --quiet");
        }

        var timingPath = Do.CreateTempFile("DressSharp-timing-", ".json");
        try
        {
            string[] arguments;
            Dictionary<string, string?>? environment = null;
            if (tool == "DressSharp")
            {
                arguments = [Do.RootDirectory / "src/DressSharp/bin/Release/net10.0/DressSharp.dll", mode];
                environment = new() { ["DRESSSHARP_BENCHMARK_WORKERS"] = workers.ToString(), ["DRESSSHARP_BENCHMARK_TIMING"] = timingPath };
            }
            else
            {
                var values = new List<string> { "format", runRoot / "Corpus.csproj", "--no-restore", "--verbosity", "quiet" };
                if (mode == "check")
                    values.Add("--verify-no-changes");
                arguments = [.. values];
            }

            var process = await Start(runRoot, "dotnet", arguments, environment);
            var accepted = tool == "dotnet format" && mode == "check" ? new[] { 0, 2 } : [0, 1];
            if (!accepted.Contains(process.ExitCode))
                throw new InvalidOperationException($"{tool} failed: {process.StandardError}");
            if (!measured)
                return null;
            var timing = timingPath.IsExistingFile && new FileInfo(timingPath).Length > 0 ? timingPath.ReadJson() : null;
            if (tool == "DressSharp" && timing is null)
                throw new InvalidOperationException($"DressSharp did not emit benchmark timing: {process.StandardError}");
            var elapsed = process.Elapsed.TotalMilliseconds;
            if (timing is JsonObject timingObject)
                timingObject["startupAndDiscoveryMilliseconds"] = Math.Max(0, elapsed - timingObject["wallMilliseconds"]!.GetValue<double>());
            return new(tool, elapsed, process.Cpu.TotalMilliseconds, process.PeakWorkingSet, process.ExitCode, timing);
        }
        finally
        {
            timingPath.Delete();
            scratch?.Delete();
        }
    }
}
finally
{
    benchmarkCorpus.Delete();
}

static SummaryResult Summary(IEnumerable<Measurement> runs, string tool)
{
    var values = runs.Where(run => run.Tool == tool).Select(run => run.WallMilliseconds).Order().ToArray();
    return new(values[values.Length / 2], values[(int)Math.Ceiling(values.Length * .95) - 1], values[0], values[^1]);
}

static JsonObject MeasurementNode(Measurement value) => new()
    {
        ["tool"] = value.Tool,
        ["wallMilliseconds"] = value.WallMilliseconds,
        ["cpuMilliseconds"] = value.CpuMilliseconds,
        ["peakWorkingSetBytes"] = value.PeakWorkingSetBytes,
        ["exitCode"] = value.ExitCode,
        ["dressSharpTiming"] = value.DressSharpTiming?.DeepClone()
    };

static JsonObject SummaryNode(SummaryResult value) => new()
    {
        ["medianMilliseconds"] = value.MedianMilliseconds,
        ["p95Milliseconds"] = value.P95Milliseconds,
        ["minimumMilliseconds"] = value.MinimumMilliseconds,
        ["maximumMilliseconds"] = value.MaximumMilliseconds
    };

static async Task<string> Capture(string command)
{
    var result = await Do.Exec(command, new() { Log = ExecLog.None });
    return string.Join(Environment.NewLine, result.AllOutput.Where(output => output.Type == OutputType.Out).Select(output => output.Message)).Trim();
}

static async Task<ProcessResult> Start(string workingDirectory, string command, string[] arguments, Dictionary<string, string?>? environment = null)
{
    var info = new ProcessStartInfo(command) { WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var argument in arguments)
        info.ArgumentList.Add(argument);
    if (environment is not null)
        foreach (var pair in environment)
            info.Environment[pair.Key] = pair.Value;
    using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {command}.");
    var output = process.StandardOutput.ReadToEndAsync();
    var error = process.StandardError.ReadToEndAsync();
    var started = DateTime.UtcNow;
    await process.WaitForExitAsync();
    process.Refresh();
    var result = new ProcessResult(process.ExitCode, DateTime.UtcNow - started, process.TotalProcessorTime, TryPeak(process), await output, await error);
    return result;
}

static long TryPeak(Process process)
{
    try
    {
        return process.PeakWorkingSet64;
    }
    catch
    {
        return 0;
    }
}

sealed record ProcessResult(int ExitCode, TimeSpan Elapsed, TimeSpan Cpu, long PeakWorkingSet, string StandardOutput, string StandardError);

sealed record Measurement(string Tool, double WallMilliseconds, double CpuMilliseconds, long PeakWorkingSetBytes, int ExitCode, JsonNode? DressSharpTiming);

sealed record SummaryResult(double MedianMilliseconds, double P95Milliseconds, double MinimumMilliseconds, double MaximumMilliseconds);
