namespace DressSharp.CommandLine;

enum CommandKind
{
    Format,
    Check,
}

sealed record CommandRequest(
    CommandKind Kind,
    IReadOnlyList<string> Paths,
    bool Verbose,
    string? ConfigurationPath);
