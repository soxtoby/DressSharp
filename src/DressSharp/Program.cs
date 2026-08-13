using System.CommandLine;
using DressSharp.Configuration;

namespace DressSharp;

internal static class Program
{
    public static Task<int> Main(string[] args) => CreateCommand().Parse(args).InvokeAsync();

    internal static RootCommand CreateCommand()
    {
        var root = new RootCommand("Format C# using explicit syntax-only preferences.");
        var target = new Argument<string?>("target") { Arity = ArgumentArity.ZeroOrOne };
        var force = new Option<bool>("--force");
        var init = new Command("init", "Write the complete Familiar preset to a managed EditorConfig block.") { target, force };
        init.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var result = await EditorConfigInitializer.InitializeAsync(
                    parseResult.GetValue(target), parseResult.GetValue(force), Environment.CurrentDirectory, cancellationToken);
                foreach (var warning in result.Warnings)
                {
                    Console.Error.WriteLine($"warning: {warning}");
                }
                return 0;
            }
            catch (ConfigurationException exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
        });
        root.Subcommands.Add(init);
        return root;
    }
}
