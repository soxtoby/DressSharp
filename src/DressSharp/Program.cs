using System.CommandLine;
using DressSharp.CommandLine;
using DressSharp.Configuration;
using DressSharp.Execution;

namespace DressSharp;

static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var invalidOption = FindInvalidOption(args);
        if (invalidOption is not null)
        {
            await Console.Error.WriteLineAsync($"Unrecognized option: {invalidOption}");
            return 2;
        }

        return await CreateCommand().Parse(args).InvokeAsync();
    }

    internal static RootCommand CreateCommand()
    {
        var verbose = new Option<bool>("--verbose") { Description = "List changed files and report an empty selection.", Recursive = true };
        var configuration = new Option<string?>("--configuration")
            {
                Description = "Use an MSBuild configuration other than Debug.",
                Recursive = true,
                Aliases = { "--config" },
            };
        var rootPaths = new Argument<string[]>("paths") { Arity = ArgumentArity.ZeroOrMore };

        var root = new RootCommand("Format C# using explicit syntax-only preferences.")
            {
                TreatUnmatchedTokensAsErrors = true,
                Options = { verbose, configuration },
                Arguments = { rootPaths },
                Subcommands =
                    {
                        CreateFileCommand("format", "Format selected C# files.", CommandKind.Format, verbose, configuration),
                        CreateFileCommand("check", "List selected C# files that require formatting.", CommandKind.Check, verbose, configuration),
                        CreateInitCommand(),
                    },
            };
        root.SetAction(parseResult => RunSelectionAsync(
            new CommandRequest(CommandKind.Format, parseResult.GetValue(rootPaths) ?? [], parseResult.GetValue(verbose), parseResult.GetValue(configuration)),
            Environment.CurrentDirectory));

        return root;
    }

    static Command CreateInitCommand()
    {
        var target = new Argument<string?>("target") { Arity = ArgumentArity.ZeroOrOne };
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
        var paths = new Argument<string[]>("paths") { Arity = ArgumentArity.ZeroOrMore };
        var command = new Command(name, description) { paths };
        command.TreatUnmatchedTokensAsErrors = true;
        command.SetAction(parseResult => RunSelectionAsync(
            new CommandRequest(kind, parseResult.GetValue(paths) ?? [], parseResult.GetValue(verbose), parseResult.GetValue(configuration)),
            Environment.CurrentDirectory));
        return command;
    }

    static async Task<int> RunSelectionAsync(CommandRequest request, string invocationDirectory)
    {
        try
        {
            var selected = await new FileSelector(invocationDirectory).SelectAsync(request.Paths);
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

    static string? FindInvalidOption(IReadOnlyList<string> args)
    {
        var afterDelimiter = false;
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (argument == "--")
            {
                afterDelimiter = true;
                continue;
            }

            if (afterDelimiter || !argument.StartsWith("-", StringComparison.Ordinal) || argument == "-")
                continue;
            if (argument is "--help" or "-h" or "-?" or "--version" or "--verbose" or "--force")
                continue;
            if (argument is "--configuration" or "--config")
            {
                index++;
                continue;
            }

            if (argument.StartsWith("--configuration=", StringComparison.Ordinal) || argument.StartsWith("--config=", StringComparison.Ordinal))
                continue;
            return argument;
        }

        return null;
    }
}