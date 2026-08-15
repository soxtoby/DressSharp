using System.Text.Json;

namespace DressSharp.Execution;

static class BenchmarkDiagnostics
{
    const string WorkerEnvironmentVariable = "DRESSSHARP_BENCHMARK_WORKERS";
    const string TimingEnvironmentVariable = "DRESSSHARP_BENCHMARK_TIMING";

    internal static bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TimingEnvironmentVariable));

    internal static int WorkerCount => int.TryParse(Environment.GetEnvironmentVariable(WorkerEnvironmentVariable), out var count) && count > 0
        ? count
        : Math.Min(Math.Max(Environment.ProcessorCount / 2, 1), 16);

    internal static async Task WriteAsync(BenchmarkTiming timing, CancellationToken cancellationToken)
    {
        var path = Environment.GetEnvironmentVariable(TimingEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(path))
            return;

        var parallelTotal = timing.Read + timing.Parse + timing.Transform + timing.Encode + timing.Write;
        var parallelWall = timing.Wall - timing.MsBuild - timing.EditorConfig;
        var scale = parallelTotal > parallelWall && parallelTotal > TimeSpan.Zero
            ? parallelWall.TotalMilliseconds / parallelTotal.TotalMilliseconds
            : 1;
        double Attributed(TimeSpan value) => value.TotalMilliseconds * scale;
        var attributed = timing.MsBuild.TotalMilliseconds + timing.EditorConfig.TotalMilliseconds +
            Attributed(timing.Read) + Attributed(timing.Parse) + Attributed(timing.Transform) + Attributed(timing.Encode) + Attributed(timing.Write);
        var payload = new
            {
                schemaVersion = 1,
                workerCount = WorkerCount,
                wallMilliseconds = timing.Wall.TotalMilliseconds,
                stages = new
                    {
                        msbuildMilliseconds = timing.MsBuild.TotalMilliseconds,
                        editorConfigMilliseconds = timing.EditorConfig.TotalMilliseconds,
                        readDecodeMilliseconds = Attributed(timing.Read),
                        parseMilliseconds = Attributed(timing.Parse),
                        transformMilliseconds = Attributed(timing.Transform),
                        encodeMilliseconds = Attributed(timing.Encode),
                        writeMilliseconds = Attributed(timing.Write),
                      otherMilliseconds = Math.Max(0, timing.Wall.TotalMilliseconds - attributed)
                    },
                rules = timing.Rules.OrderByDescending(pair => pair.Value).ToDictionary(pair => pair.Key, pair => pair.Value.TotalMilliseconds)
            };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(payload), cancellationToken);
    }
}
