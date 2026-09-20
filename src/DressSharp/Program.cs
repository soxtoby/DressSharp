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
        var scope = CreateScopeOptions();
        var verbose = new Option<bool>("--verbose") { Description = "List changed files and report an empty selection.", Recursive = true };
        var configuration = new Option<string?>("--configuration")
            {
                Description = "Use an MSBuild configuration other than Debug.",
                Recursive = true,
            };

        var root = new RootCommand("Format C# using explicit syntax-only preferences.")
            {
                TreatUnmatchedTokensAsErrors = true,
                Options = { includes, scope.Staged, scope.Changed, verbose, configuration },
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
                    foreach (var option in new Option[] { includes, scope.Staged, scope.Changed })
                    {
                        if (result.GetResult(option) is not null)
                            result.AddError($"Option '{option.Name}' must follow an explicit command.");
                    }
                });
        }
        root.Validators.Add(scope.Validate);
        root.SetAction(parseResult => RunSelectionAsync(
            new CommandRequest(
                CommandKind.Format,
                parseResult.GetValue(includes) ?? [],
                parseResult.GetValue(verbose),
                parseResult.GetValue(configuration),
                scope.Read(parseResult)),
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
        var scope = CreateScopeOptions();
        var command = new Command(name, description) { includes, scope.Staged, scope.Changed };
        command.TreatUnmatchedTokensAsErrors = true;
        command.Validators.Add(scope.Validate);
        command.SetAction(parseResult => RunSelectionAsync(
            new CommandRequest(
                kind,
                parseResult.GetValue(includes) ?? [],
                parseResult.GetValue(verbose),
                parseResult.GetValue(configuration),
                scope.Read(parseResult)),
            Environment.CurrentDirectory));
        return command;
    }

    static ScopeOptions CreateScopeOptions() => new(
        new("--staged") { Description = "Select only files whose staged content differs from HEAD." },
        new("--changed") { Description = "Select only files changed since HEAD, including untracked files." });

    sealed record ScopeOptions(Option<bool> Staged, Option<bool> Changed)
    {
        internal void Validate(CommandResult result)
        {
            if (result.GetResult(Staged) is not null && result.GetResult(Changed) is not null)
                result.AddError("Options '--staged' and '--changed' cannot be combined.");
        }

        internal SelectionScope Read(ParseResult result) => result.GetValue(Staged) ? SelectionScope.Staged
            : result.GetValue(Changed) ? SelectionScope.Changed
            : SelectionScope.All;
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
            var selected = await new FileSelector(invocationDirectory).Select(request.Includes, request.Scope, CancellationToken.None);
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
