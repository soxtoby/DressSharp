using System.CommandLine;
using DressSharp.CommandLine;
using DressSharp.Configuration;
using DressSharp.Execution;

namespace DressSharp;

static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var result = CreateCommand().Parse(args);
        var exitCode = await result.InvokeAsync();
        return result.Errors.Count == 0 ? exitCode : 2;
    }

    internal static RootCommand CreateCommand()
    {
        var includes = CreateIncludeOption();
        var verbose = new Option<bool>("--verbose") { Description = "List changed files and report an empty selection.", Recursive = true };
        var configuration = new Option<string?>("--configuration")
            {
                Description = "Use an MSBuild configuration other than Debug.",
                Recursive = true,
                Aliases = { "--config" },
            };

        var root = new RootCommand("Format C# using explicit syntax-only preferences.")
            {
                TreatUnmatchedTokensAsErrors = true,
                Options = { includes, verbose, configuration },
                Subcommands =
                    {
                        CreateFileCommand("format", "Format selected C# files.", CommandKind.Format, verbose, configuration),
                        CreateFileCommand("check", "List selected C# files that require formatting.", CommandKind.Check, verbose, configuration),
                        CreateInitCommand(),
                    },
            };
        foreach (var command in root.Subcommands)
        {
            command.Validators.Add(result =>
                {
                    if (result.GetResult(includes) is not null)
                        result.AddError("Option '--include' must follow an explicit command.");
                });
        }
        root.SetAction(parseResult => RunSelectionAsync(
            new CommandRequest(CommandKind.Format, parseResult.GetValue(includes) ?? [], parseResult.GetValue(verbose), parseResult.GetValue(configuration)),
            Environment.CurrentDirectory));

        return root;
    }

    static Command CreateInitCommand()
    {
        var target = new Option<string?>("--target") { Description = "EditorConfig file to initialize." };
        var force = new Option<bool>("--force");
        var init = new Command("init", "Write the complete Familiar preset to a managed EditorConfig block.") { target, force };
        init.SetAction(async (parseResult, cancellationToken) =>
            {
                try
                {
                    var result = await EditorConfigInitializer.InitializeAsync(
                        parseResult.GetValue(target),
                        parseResult.GetValue(force),
                        Environment.CurrentDirectory,
                        cancellationToken);
                    foreach (var warning in result.Warnings)
                    {
                        await Console.Error.WriteLineAsync($"warning: {warning}");
                    }

                    return 0;
                }
                catch (ConfigurationException exception)
                {
                    await Console.Error.WriteLineAsync(exception.Message);
                    return 2;
                }
            });
        return init;
    }

    static Command CreateFileCommand(
        string name,
        string description,
        CommandKind kind,
        Option<bool> verbose,
        Option<string?> configuration)
    {
        var includes = CreateIncludeOption();
        var command = new Command(name, description) { includes };
        command.TreatUnmatchedTokensAsErrors = true;
        command.SetAction(parseResult => RunSelectionAsync(
            new CommandRequest(
                kind,
                parseResult.GetValue(includes) ?? [],
                parseResult.GetValue(verbose),
                parseResult.GetValue(configuration)),
            Environment.CurrentDirectory));
        return command;
    }

    static Option<string[]> CreateIncludeOption() => new("--include")
        {
            Description = "Select an invocation-directory-relative glob. Repeat to combine selections.",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = false,
        };

    static async Task<int> RunSelectionAsync(CommandRequest request, string invocationDirectory)
    {
        try
        {
            var selected = await new FileSelector(invocationDirectory).SelectAsync(request.Includes);
            if (request.Verbose && selected.Count == 0)
                await Console.Out.WriteLineAsync("No eligible C# files selected.");
            return await new FormatExecutor(invocationDirectory).RunAsync(request, selected);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await Console.Error.WriteLineAsync(exception.Message);
            return 2;
        }
    }
}
