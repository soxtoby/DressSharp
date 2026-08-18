namespace DressSharp.CommandLine;

enum CommandKind
{
    Format,
    Check,
}

sealed record CommandRequest(
    CommandKind Kind,
    IReadOnlyList<string> Includes,
    bool Verbose,
    string? BuildConfiguration);
