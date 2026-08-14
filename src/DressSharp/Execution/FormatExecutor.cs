using DressSharp.Architecture;
using DressSharp.CommandLine;
using DressSharp.Configuration;
using DressSharp.IO;
using DressSharp.Parsing;
using DressSharp.Rules;
using Microsoft.CodeAnalysis.CSharp;
using System.Diagnostics;

namespace DressSharp.Execution;

sealed class FormatExecutor(string invocationDirectory, TextWriter? output = null, TextWriter? error = null)
{
    readonly string _invocationDirectory = Path.GetFullPath(invocationDirectory);
    readonly TextWriter _output = output ?? Console.Out;
    readonly TextWriter _error = error ?? Console.Error;

    internal async Task<int> RunAsync(CommandRequest request, IReadOnlyList<SelectedFile> selected, CancellationToken cancellationToken = default)
    {
        var timing = new BenchmarkTiming();
        var commandStart = Stopwatch.GetTimestamp();
        if (selected.Count == 0)
            return 0;

        var paths = selected.Select(file => file.FullPath).ToArray();
        var stageStart = Stopwatch.GetTimestamp();
        var configurations = await ConfigurationPreflight.ResolveAllAsync(paths, new EditorConfigResolver(), cancellationToken);
        timing.EditorConfig = Stopwatch.GetElapsedTime(stageStart);
        stageStart = Stopwatch.GetTimestamp();
        var contexts = await new ParseContextResolver(_invocationDirectory).ResolveAsync(paths, request.BuildConfiguration, cancellationToken);
        timing.MsBuild = Stopwatch.GetElapsedTime(stageStart);

        var prepared = new PreparedFile?[selected.Count];
        var failed = false;
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
                    var context = contexts[file.FullPath];
                    if (!context.CanFormat)
                        return;
                    try
                    {
                        var readStart = Stopwatch.GetTimestamp();
                        var document = await SourceDocument.ReadAsync(file.FullPath, token);
                        timing.AddRead(Stopwatch.GetElapsedTime(readStart));
                        var parseStart = Stopwatch.GetTimestamp();
                        var tree = CSharpSyntaxTree.ParseText(document.Text, context.Options!, file.FullPath, cancellationToken: token);
                        var root = await tree.GetRootAsync(token);
                        timing.AddParse(Stopwatch.GetElapsedTime(parseStart));
                        var transformStart = Stopwatch.GetTimestamp();
                        var transformation = new TransformationPipeline(RuleCatalog.BuiltIn)
                            .Transform(root, configurations[file.FullPath]);
                        timing.AddTransform(Stopwatch.GetElapsedTime(transformStart));
                        if (!transformation.Succeeded)
                            throw transformation.Failure!;
                        var encodeStart = Stopwatch.GetTimestamp();
                        var bytes = document.Encode(transformation.Root.ToFullString(), Representation(configurations[file.FullPath]));
                        timing.AddEncode(Stopwatch.GetElapsedTime(encodeStart));
                        prepared[index] = new(document, bytes, transformation.SkippedOccurrences);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        prepared[index] = new(exception);
                    }
                });

        for (var index = 0; index < selected.Count; index++)
        {
            var file = selected[index];
            foreach (var diagnostic in contexts[file.FullPath].Diagnostics)
            {
                if (request.Verbose || !contexts[file.FullPath].CanFormat)
                    await _error.WriteLineAsync(diagnostic);
            }

            if (!contexts[file.FullPath].CanFormat)
            {
                failed = true;
                continue;
            }

            var result = prepared[index]!;
            if (result.Failure is not null)
            {
                await _error.WriteLineAsync($"{file.DisplayPath}: {result.Failure.Message}");
                failed = true;
                continue;
            }

            if (request.Verbose && result.SkippedOccurrences > 0)
                await _error.WriteLineAsync($"{file.DisplayPath}: skipped {result.SkippedOccurrences} malformed occurrence(s).");
            if (result.Content.Span.SequenceEqual(result.Document!.OriginalBytes.Span))
                continue;

            if (request.Kind == CommandKind.Check || request.Verbose)
                await _output.WriteLineAsync(file.DisplayPath);
            if (request.Kind == CommandKind.Check)
                continue;
            
            try
            {
                var writeStart = Stopwatch.GetTimestamp();
                await new AtomicFilePersistence().WriteIfChangedAsync(result.Document, result.Content, cancellationToken);
                timing.AddWrite(Stopwatch.GetElapsedTime(writeStart));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await _error.WriteLineAsync($"{file.DisplayPath}: {exception.Message}");
                failed = true;
            }
        }

        var exitCode = failed ? 2 : request.Kind == CommandKind.Check && prepared.Any(result => result?.Changed == true) ? 1 : 0;
        timing.Wall = Stopwatch.GetElapsedTime(commandStart);
        await BenchmarkDiagnostics.WriteAsync(timing, cancellationToken);
        return exitCode;
    }

    static RepresentationPreferences Representation(FormattingConfiguration configuration) => new(
        Encoding(configuration.Preferences.GetValueOrDefault("charset")),
        LineEnding(configuration.Preferences.GetValueOrDefault("end_of_line")),
        Boolean(configuration.Preferences.GetValueOrDefault("insert_final_newline")),
        Boolean(configuration.Preferences.GetValueOrDefault("trim_trailing_whitespace")));

    static SourceEncoding? Encoding(string? value) => value switch
        {
            "utf-8" => SourceEncoding.Utf8,
            "utf-8-bom" => SourceEncoding.Utf8Bom,
            "utf-16le" => SourceEncoding.Utf16LittleEndian,
            "utf-16be" => SourceEncoding.Utf16BigEndian,
            "latin1" => SourceEncoding.Latin1,
            _ => null
        };

    static string? LineEnding(string? value) => value switch
        {
            "lf" => "\n",
            "crlf" => "\r\n",
            "cr" => "\r",
            _ => null
        };

    static bool? Boolean(string? value) => bool.TryParse(value, out var parsed) ? parsed : null;

    sealed class PreparedFile
    {
        internal PreparedFile(SourceDocument document, byte[] content, int skippedOccurrences)
        {
            Document = document;
            Content = content;
            SkippedOccurrences = skippedOccurrences;
        }

        internal PreparedFile(Exception failure) => Failure = failure;

        internal SourceDocument? Document { get; }
        internal ReadOnlyMemory<byte> Content { get; }
        internal Exception? Failure { get; }
        internal int SkippedOccurrences { get; }
        internal bool Changed => Failure is null && !Content.Span.SequenceEqual(Document!.OriginalBytes.Span);
    }
}

sealed class BenchmarkTiming
{
    readonly object _gate = new();
    TimeSpan _read;
    TimeSpan _parse;
    TimeSpan _transform;
    TimeSpan _encode;
    TimeSpan _write;

    internal TimeSpan Wall { get; set; }
    internal TimeSpan MsBuild { get; set; }
    internal TimeSpan EditorConfig { get; set; }
    internal TimeSpan Read => _read;
    internal TimeSpan Parse => _parse;
    internal TimeSpan Transform => _transform;
    internal TimeSpan Encode => _encode;
    internal TimeSpan Write => _write;

    internal void AddRead(TimeSpan value) => Add(ref _read, value);
    internal void AddParse(TimeSpan value) => Add(ref _parse, value);
    internal void AddTransform(TimeSpan value) => Add(ref _transform, value);
    internal void AddEncode(TimeSpan value) => Add(ref _encode, value);
    internal void AddWrite(TimeSpan value) => Add(ref _write, value);

    void Add(ref TimeSpan target, TimeSpan value)
    {
        lock (_gate)
            target += value;
    }
}
