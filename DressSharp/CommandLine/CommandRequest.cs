namespace DressSharp.CommandLine;

sealed record CommandRequest(
    CommandKind Kind,
    IReadOnlyList<string> Includes,
    bool Verbose,
    string? BuildConfiguration,
    SelectionScope Scope = SelectionScope.All);

/// <summary>Which files an include may select: every eligible file, or only those Git reports as changed.</summary>
enum SelectionScope
{
    All,
    /// <summary>Files whose staged content differs from HEAD.</summary>
    Staged,
    /// <summary>Files whose working-tree content differs from HEAD, plus nonignored untracked files.</summary>
    Changed,
}

enum CommandKind
{
    Format,
    Check,
}
