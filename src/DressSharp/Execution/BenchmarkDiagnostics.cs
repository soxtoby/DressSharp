using System.Text.Json;

namespace DressSharp.Execution;

static class BenchmarkDiagnostics
{
    const string WorkerEnvironmentVariable = "DRESSSHARP_BENCHMARK_WORKERS";
    const string TimingEnvironmentVariable = "DRESSSHARP_BENCHMARK_TIMING";

    // Formatting a file is processor-bound and independent of every other file, so a run has as
    // much of the machine as it can keep busy. The cap is for memory: each worker holds a file's
    // trees and its emitted text, and past a point more of those in flight buys nothing.
    internal static int WorkerCount => int.TryParse(Environment.GetEnvironmentVariable(WorkerEnvironmentVariable), out var count) && count > 0
        ? count
        : Math.Min(Math.Max(Environment.ProcessorCount, 1), 32);

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
                }
            };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(payload), cancellationToken);
    }
}

sealed class BenchmarkTiming
{
    long _readTicks;
    long _parseTicks;
    long _transformTicks;
    long _encodeTicks;
    long _writeTicks;

    internal TimeSpan Wall { get; set; }
    internal TimeSpan MsBuild { get; set; }
    internal TimeSpan EditorConfig { get; set; }
    internal TimeSpan Read => TimeSpan.FromTicks(Volatile.Read(ref _readTicks));
    internal TimeSpan Parse => TimeSpan.FromTicks(Volatile.Read(ref _parseTicks));
    internal TimeSpan Transform => TimeSpan.FromTicks(Volatile.Read(ref _transformTicks));
    internal TimeSpan Encode => TimeSpan.FromTicks(Volatile.Read(ref _encodeTicks));
    internal TimeSpan Write => TimeSpan.FromTicks(Volatile.Read(ref _writeTicks));

    internal void AddRead(TimeSpan value) => Add(ref _readTicks, value);
    internal void AddParse(TimeSpan value) => Add(ref _parseTicks, value);
    internal void AddTransform(TimeSpan value) => Add(ref _transformTicks, value);
    internal void AddEncode(TimeSpan value) => Add(ref _encodeTicks, value);
    internal void AddWrite(TimeSpan value) => Add(ref _writeTicks, value);
    static void Add(ref long target, TimeSpan value) => Interlocked.Add(ref target, value.Ticks);
}
