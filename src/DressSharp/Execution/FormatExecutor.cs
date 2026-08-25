using DressSharp.Architecture;
using DressSharp.CommandLine;
using DressSharp.Configuration;
using DressSharp.IO;
using DressSharp.Parsing;
using System.Diagnostics;

namespace DressSharp.Execution;

sealed class FormatExecutor(string invocationDirectory, TextWriter? output = null, TextWriter? error = null)
{
    readonly string _invocationDirectory = Path.GetFullPath(invocationDirectory);
    readonly TextWriter _output = output ?? Console.Out;
    readonly TextWriter _error = error ?? Console.Error;

    internal async Task<int> Run(CommandRequest request, IReadOnlyList<SelectedFile> selected, CancellationToken cancellationToken = default)
    {
        var timing = new BenchmarkTiming();
        var commandStart = Stopwatch.GetTimestamp();
        if (selected.Count == 0)
        {
            await ReportFormatSummary(request, 0, 0, Stopwatch.GetElapsedTime(commandStart));
            return 0;
        }

        var preparation = await PrepareRun(request, selected, timing, cancellationToken);
        var prepared = await FormatFiles(selected, preparation, timing, cancellationToken);
        var failed = await ReportAndPersist(request, selected, preparation.Contexts, prepared, timing, cancellationToken);

        timing.Wall = Stopwatch.GetElapsedTime(commandStart);
        await BenchmarkDiagnostics.WriteAsync(timing, cancellationToken);
        if (!failed)
            await ReportFormatSummary(request, prepared.Count(result => result?.Changed == true), selected.Count, timing.Wall);
        return ExitCode(request, prepared, failed);
    }

    async ValueTask ReportFormatSummary(CommandRequest request, int formatted, int selected, TimeSpan elapsed)
    {
        if (request.Kind != CommandKind.Format)
            return;

        var files = selected == 1 ? "file" : "files";
        var summary = FormattableString.Invariant($"Formatted {formatted} of {selected} {files} in {elapsed.TotalSeconds:F2} s.");
        await _output.WriteLineAsync(summary);
    }

    async ValueTask<RunPreparation> PrepareRun(
        CommandRequest request,
        IReadOnlyList<SelectedFile> selected,
        BenchmarkTiming timing,
        CancellationToken cancellationToken)
    {
        var paths = selected.Select(file => file.FullPath).ToArray();
        var stageStart = Stopwatch.GetTimestamp();
        var configurations = new EditorConfigResolver().ResolveAll(paths, cancellationToken);
        timing.EditorConfig = Stopwatch.GetElapsedTime(stageStart);
        if (configurations.Values.All(configuration => configuration.Preferences.Count == 0))
            await _error.WriteLineAsync("warning: no EditorConfig preferences to apply; run 'dotnet dress init' to initialize .editorconfig.");

        stageStart = Stopwatch.GetTimestamp();
        var contexts = await new ParseContextResolver(_invocationDirectory).Resolve(paths, request.BuildConfiguration, cancellationToken);
        timing.MsBuild = Stopwatch.GetElapsedTime(stageStart);

        return new(contexts, CreateFormatters(selected, configurations, timing));
    }

    static DocumentFormatter[] CreateFormatters(
        IReadOnlyList<SelectedFile> selected,
        IReadOnlyDictionary<string, FormattingConfiguration> configurations,
        BenchmarkTiming timing)
    {
        var formatters = new DocumentFormatter[selected.Count];
        var byConfiguration = new Dictionary<FormattingConfiguration, DocumentFormatter>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < selected.Count; index++)
        {
            var configuration = configurations[selected[index].FullPath];
            if (!byConfiguration.TryGetValue(configuration, out var formatter))
            {
                formatter = new(configuration, timing);
                byConfiguration.Add(configuration, formatter);
            }

            formatters[index] = formatter;
        }

        return formatters;
    }

    static async ValueTask<PreparedFile?[]> FormatFiles(
        IReadOnlyList<SelectedFile> selected,
        RunPreparation preparation,
        BenchmarkTiming timing,
        CancellationToken cancellationToken)
    {
        var prepared = new PreparedFile?[selected.Count];
        var parallelOptions = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = BenchmarkDiagnostics.WorkerCount
            };
        await Parallel.ForEachAsync(
            Enumerable.Range(0, selected.Count),
            parallelOptions,
            async (index, token) =>
                {
                    var file = selected[index];
                    prepared[index] = await FormatFile(
                        file,
                        preparation.Contexts[file.FullPath],
                        preparation.Formatters[index],
                        timing,
                        token);
                });

        return prepared;
    }

    static async ValueTask<PreparedFile?> FormatFile(
        SelectedFile file,
        ParseContextResolution context,
        DocumentFormatter formatter,
        BenchmarkTiming timing,
        CancellationToken cancellationToken)
    {
        if (!context.CanFormat)
            return null;

        try
        {
            var readStart = Stopwatch.GetTimestamp();
            var document = await SourceDocument.ReadAsync(file.FullPath, cancellationToken);
            timing.AddRead(Stopwatch.GetElapsedTime(readStart));
            var formatted = await formatter.Format(document, context.Options!, cancellationToken);
            return new(document, formatted);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(exception);
        }
    }

    async ValueTask<bool> ReportAndPersist(
        CommandRequest request,
        IReadOnlyList<SelectedFile> selected,
        IReadOnlyDictionary<string, ParseContextResolution> contexts,
        IReadOnlyList<PreparedFile?> prepared,
        BenchmarkTiming timing,
        CancellationToken cancellationToken)
    {
        var failed = false;
        for (var index = 0; index < selected.Count; index++)
        {
            var file = selected[index];
            var succeeded = await ReportAndPersistFile(
                request,
                file,
                contexts[file.FullPath],
                prepared[index],
                timing,
                cancellationToken);
            if (!succeeded)
                failed = true;
        }

        return failed;
    }

    async ValueTask<bool> ReportAndPersistFile(
        CommandRequest request,
        SelectedFile file,
        ParseContextResolution context,
        PreparedFile? prepared,
        BenchmarkTiming timing,
        CancellationToken cancellationToken)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            if (request.Verbose || !context.CanFormat)
                await _error.WriteLineAsync(diagnostic);
        }

        if (!context.CanFormat)
            return false;

        var result = prepared!;
        if (result.Failure is not null)
        {
            await _error.WriteLineAsync($"{file.DisplayPath}: {result.Failure.Message}");
            return false;
        }

        if (request.Verbose && result.SkippedOccurrences > 0)
            await _error.WriteLineAsync($"{file.DisplayPath}: skipped {result.SkippedOccurrences} malformed occurrence(s).");
        if (!result.Changed)
            return true;

        if (request.Kind == CommandKind.Check || request.Verbose)
            await _output.WriteLineAsync(file.DisplayPath);
        if (request.Kind == CommandKind.Check)
            return true;

        return await Persist(file, result, timing, cancellationToken);
    }

    async ValueTask<bool> Persist(
        SelectedFile file,
        PreparedFile result,
        BenchmarkTiming timing,
        CancellationToken cancellationToken)
    {
        try
        {
            var writeStart = Stopwatch.GetTimestamp();
            await new AtomicFilePersistence().WriteIfChangedAsync(result.Document!, result.Content, cancellationToken);
            timing.AddWrite(Stopwatch.GetElapsedTime(writeStart));
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await _error.WriteLineAsync($"{file.DisplayPath}: {exception.Message}");
            return false;
        }
    }

    static int ExitCode(CommandRequest request, IReadOnlyList<PreparedFile?> prepared, bool failed)
    {
        if (failed)
            return 2;
        return request.Kind == CommandKind.Check && prepared.Any(result => result?.Changed == true) ? 1 : 0;
    }

    sealed record RunPreparation(
        IReadOnlyDictionary<string, ParseContextResolution> Contexts,
        DocumentFormatter[] Formatters);

    sealed class PreparedFile
    {
        internal PreparedFile(SourceDocument document, FormattedDocument formatted)
        {
            Document = document;
            Content = formatted.Content;
            SkippedOccurrences = formatted.SkippedOccurrences;
        }

        internal PreparedFile(Exception failure) => Failure = failure;

        internal SourceDocument? Document { get; }
        internal ReadOnlyMemory<byte> Content { get; }
        internal Exception? Failure { get; }
        internal int SkippedOccurrences { get; }
        internal bool Changed => Failure is null && !Content.Span.SequenceEqual(Document!.OriginalBytes.Span);
    }
}
