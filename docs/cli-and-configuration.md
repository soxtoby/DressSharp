# CLI and configuration

This document describes DressSharp 0.x. The executable is distributed by the `DressSharp` .NET tool package and invoked as `dotnet dress`.

## Commands

```text
dotnet dress [--include <pattern>]... [--verbose] [--configuration <name>]
dotnet dress format [--include <pattern>]... [--verbose] [--configuration <name>]
dotnet dress check [--include <pattern>]... [--verbose] [--configuration <name>]
dotnet dress init [--target <path>] [--force]
```

The root command aliases `format`. An omitted include selects `**/*.cs` beneath the invocation directory. `--include` accepts an invocation-directory-relative glob and may be repeated; an exact file name is also a valid pattern. With an explicit command, place `--include` after the command name. Patterns use `/`, `*`, and `**`; quote them to prevent shell expansion. They cannot leave the invocation directory.

Inside a Git worktree, DressSharp selects tracked files plus nonignored untracked files using Git's standard excludes, including nested `.gitignore`, `.git/info/exclude`, and the user's global excludes. Outside a Git worktree it searches the filesystem directly. Both modes include only ordinary `.cs` files and exclude generated files, `.csx`, VCS directories, and linked paths. Overlapping includes are deduplicated.

`format` writes changed files and reports the changed count and elapsed time. `check` writes nothing and lists files requiring changes. `--verbose` lists changed files and reports an empty selection. `--configuration` (alias `--config`) selects the MSBuild configuration; the default is `Debug`.

`init` writes the complete Default preferences into one EOF block delimited by `# DressSharp Begin` and `# DressSharp End`. `--target` defaults to `.editorconfig` in the invocation directory. Existing conflicting keys require `--force`; malformed or duplicate managed markers always fail.

## Exit codes

| Code | Meaning |
| ---: | --- |
| 0 | Command succeeded; `check` found no changes. |
| 1 | `check` found files requiring formatting. |
| 2 | Invalid invocation, configuration, selection, parse context, or processing failure. |

## Configuration

DressSharp resolves EditorConfig independently for each file using ordinary traversal, `root = true`, section matching, precedence, and `unset`. Missing or `unset` preferences do nothing: DressSharp has no implicit formatting defaults. `init` provides the explicit Default preferences.

Known invalid effective values and malformed EditorConfig fail preflight before source writes. Unknown well-formed keys are ignored for forward compatibility. Standard C# values may include a diagnostic severity suffix such as `:warning`; DressSharp ignores that suffix.

See [rules-v1.md](rules-v1.md) for supported keys and values.

## Safety and file handling

DressSharp uses syntax only—never symbols, types, or semantic models. It derives language version, preprocessor symbols, source kind, and documentation mode from MSBuild. Unsupported project language versions fail before writes. Unsafe individual occurrences intersecting malformed syntax, directives, or disabled text are skipped; safe occurrences continue.

Source decoding checks a BOM, then strict UTF-8, then strict Latin-1. BOM-less UTF-16 is not guessed. Unchanged files retain their original bytes. Changed files use same-directory atomic replacement, reject changed-since-read content, and protect symlink targets.
