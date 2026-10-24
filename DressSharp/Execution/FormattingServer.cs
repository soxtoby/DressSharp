using System.Text.Json;
using System.Text.Json.Serialization;
using DressSharp.CommandLine;

namespace DressSharp.Execution;

/// <summary>Formats documents and saved files over a versioned JSON stream until its owner closes stdin.</summary>
static class FormattingServer
{
    internal static async Task Run(
        string invocationDirectory,
        TextReader input,
        TextWriter output,
        string? configuration = null,
        bool verbose = false,
        CancellationToken cancellationToken = default)
    {
        var ready = new FormattingReady(2, Environment.ProcessId, typeof(FormattingServer).Assembly.Location);
        await output.WriteLineAsync(JsonSerializer.Serialize(ready, FormattingJson.Default.FormattingReady));
        await output.FlushAsync(cancellationToken);

        while (await input.ReadLineAsync(cancellationToken) is { } line)
        {
            var response = await Format(invocationDirectory, line, configuration, verbose, cancellationToken);
            await output.WriteLineAsync(JsonSerializer.Serialize(response, FormattingJson.Default.FormattingResponse));
            await output.FlushAsync(cancellationToken);
        }
    }

    static async Task<FormattingResponse> Format(
        string invocationDirectory,
        string line,
        string? configuration,
        bool verbose,
        CancellationToken cancellationToken)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            var request = JsonSerializer.Deserialize(line, FormattingJson.Default.FormattingRequest);
            int exitCode;
            if (request is { Includes.Length: > 0, Path: null, Text: null } && request.Includes.All(include => !string.IsNullOrWhiteSpace(include)))
            {
                var command = new CommandRequest(CommandKind.Format, request.Includes, true, configuration);
                var selected = await new FileSelector(invocationDirectory).Select(command.Includes, command.Scope, cancellationToken);
                exitCode = await new FormatExecutor(invocationDirectory, output, error).Run(command, selected, cancellationToken);
            }
            else if (request is { Includes: null, Text: not null } && !string.IsNullOrWhiteSpace(request.Path))
            {
                using var input = new StringReader(request.Text);
                exitCode = await new FormatExecutor(invocationDirectory, TextWriter.Null, error).RunStandardInput(
                    request.Path,
                    configuration,
                    verbose,
                    input,
                    output,
                    cancellationToken);
            }
            else
            {
                return new(2, "", "A formatting request must contain either a path and text, or nonempty includes.");
            }

            return new(exitCode, output.ToString(), error.ToString());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(2, "", exception.Message);
        }
    }
}

sealed record FormattingReady(int Protocol, int ProcessId, string AssemblyPath);

sealed record FormattingRequest(string? Path = null, string? Text = null, string[]? Includes = null);

sealed record FormattingResponse(int ExitCode, string Stdout, string Stderr);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(FormattingReady))]
[JsonSerializable(typeof(FormattingRequest))]
[JsonSerializable(typeof(FormattingResponse))]
partial class FormattingJson : JsonSerializerContext;
