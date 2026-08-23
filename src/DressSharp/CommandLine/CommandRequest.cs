namespace DressSharp.CommandLine;

sealed record CommandRequest(
    CommandKind Kind,
    IReadOnlyList<string> Includes,
    bool Verbose,
    string? BuildConfiguration);

enum CommandKind
{
    Format,
    Check,
}
