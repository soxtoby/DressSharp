using System.CommandLine;

namespace DressSharp;

internal static class Program
{
    public static Task<int> Main(string[] args) => CreateCommand().Parse(args).InvokeAsync();

    internal static RootCommand CreateCommand()
    {
        return new RootCommand("Format C# using explicit syntax-only preferences.");
    }
}
