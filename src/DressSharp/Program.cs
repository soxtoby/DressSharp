using System.CommandLine;
using System.CommandLine.Parsing;
using DressSharp.CommandLine;
using DressSharp.Configuration;
using DressSharp.Execution;
using DressSharp.Interactive;

namespace DressSharp;

static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var result = CreateCommand().Parse(args);
        var exitCode = await result.InvokeAsync();
        return result.Errors.Count == 0 ? exitCode : 2;
    }

    internal static RootCommand CreateCommand(IInteractiveApplication? interactiveApplication = null, TextWriter? error = null)
    {
        interactiveApplication ??= InteractiveApplication.CreateDefault();
        error ??= Console.Error;
        var includes = CreateIncludeOption();
        var verbose = new Option<bool>("--verbose") { Description = "List changed files and report an empty selection.", Recursive = true };
        var configuration = new Option<string?>("--configuration")
            {
                Description = "Use an MSBuild configuration other than Debug.",
                Recursive = true,
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
                        CreateInteractiveCommand(interactiveApplication, error, includes, verbose, configuration),
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

    static Command CreateInteractiveCommand(
        IInteractiveApplication application,
        TextWriter error,
        Option<string[]> includes,
        Option<bool> verbose,
        Option<string?> configuration)
    {
        var config = new Option<string?>("--config") { Description = "Select an EditorConfig file or directory." };
        var interactive = new Command("interactive", "Open the local interactive configuration application.") { config };
        interactive.Validators.Add(result =>
            {
                RejectInheritedOption(result, includes);
                RejectInheritedOption(result, verbose);
                RejectInheritedOption(result, configuration);
            });
        interactive.SetAction(async (parseResult, cancellationToken) =>
            {
                try
                {
                    await application.Run(parseResult.GetValue(config), Environment.CurrentDirectory, cancellationToken);
                    return 0;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return 0;
                }
                catch (Exception exception)
                {
                    await error.WriteLineAsync(exception.Message);
                    return 2;
                }
            });
        return interactive;

        static void RejectInheritedOption<T>(CommandResult result, Option<T> option)
        {
            if (result.GetResult(option) is not null)
                result.AddError($"Option '{option.Name}' is not valid for command 'interactive'.");
        }
    }

    static Command CreateInitCommand()
    {
        var target = new Option<string?>("--target") { Description = "EditorConfig file to initialize." };
        var init = new Command("init", "Add missing Default preferences to an EditorConfig file.") { target };
        init.SetAction(async (parseResult, cancellationToken) =>
            {
                try
                {
                    await EditorConfigInitializer.InitializeAsync(
                        parseResult.GetValue(target),
                        Environment.CurrentDirectory,
                        cancellationToken);
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
            var selected = await new FileSelector(invocationDirectory).Select(request.Includes);
            if (request.Verbose && selected.Count == 0)
                await Console.Out.WriteLineAsync("No eligible C# files selected.");
            return await new FormatExecutor(invocationDirectory).Run(request, selected);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await Console.Error.WriteLineAsync(exception.Message);
            return 2;
        }
    }
}
