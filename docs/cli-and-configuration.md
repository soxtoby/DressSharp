# CLI and configuration

This document describes DressSharp 0.x. The executable is distributed by the `DressSharp` .NET tool package and invoked as `dotnet dress`.

## Commands

```text
dotnet dress [--include <pattern>]... [--verbose] [--configuration <name>]
dotnet dress format [--include <pattern>]... [--verbose] [--configuration <name>]
dotnet dress check [--include <pattern>]... [--verbose] [--configuration <name>]
dotnet dress init [--target <path>]
dotnet dress interactive [--config <path>]
```

The root command aliases `format`. An omitted include selects `**/*.cs` beneath the invocation directory. `--include` accepts an invocation-directory-relative glob and may be repeated; an exact file name is also a valid pattern. With an explicit command, place `--include` after the command name. Patterns use `/`, `*`, and `**`; quote them to prevent shell expansion. They cannot leave the invocation directory.

Inside a Git worktree, DressSharp selects tracked files plus nonignored untracked files using Git's standard excludes, including nested `.gitignore`, `.git/info/exclude`, and the user's global excludes. Outside a Git worktree it searches the filesystem directly. Both modes include only ordinary `.cs` files and exclude generated files, `.csx`, VCS directories, and linked paths. Overlapping includes are deduplicated.

`format` writes changed files and reports the changed count and elapsed time. `check` writes nothing and lists files requiring changes. `--verbose` lists changed files and reports an empty selection. `--configuration` selects the MSBuild configuration; the default is `Debug`.

`init` adds each missing supported preference to an EditorConfig file. An assignment in any section counts as present and remains unchanged. Missing preferences are appended to the last `[*.cs]` section, or to a new `[*.cs]` section at the end when none exists. `--target` defaults to `.editorconfig` in the invocation directory.

`interactive` starts the offline browser application on a random loopback port and opens it in the default browser. Press Ctrl+C or use the application's stop button to shut it down. `--config` selects an EditorConfig file or directory; without it, DressSharp discovers the nearest `.editorconfig` from the invocation directory.

Edit the preview sample or paste C# into the left pane, then change preferences to compare real formatted output on the right. Preview updates automatically and never saves source. The Whitespace toggle reveals spaces, tabs, and line endings; output metadata shows encoding and final-newline changes. Pasted source uses LF, and preview uses the bundled formatter's latest stable C# version with no predefined symbols. External configuration changes enter the preview when you choose Reload; pending edits remain applied.

## Exit codes

| Code | Meaning |
| ---: | --- |
| 0 | Command succeeded; `check` found no changes. |
| 1 | `check` found files requiring formatting. |
| 2 | Invalid invocation, configuration, selection, parse context, or processing failure. |

## Configuration

DressSharp resolves EditorConfig independently for each file using ordinary traversal, `root = true`, section matching, precedence, and `unset`. Missing or `unset` preferences do nothing: DressSharp has no implicit formatting defaults. `init` provides the explicit Default preferences.

Known invalid effective values and malformed EditorConfig fail preflight before source writes. Unknown well-formed keys are ignored for forward compatibility. Standard C# values may include a diagnostic severity suffix such as `:warning`; DressSharp ignores that suffix.

`dotnet dress init` writes every supported key with its Default value. The versioned rule catalog is the authoritative source for supported values and interactive documentation.

### Lambda block indentation

Use `dress_lambda_block_indentation = indented` to place multiline lambda braces one indentation level below the lambda's starting line:

```csharp
var callback = () =>
    {
        Work();
        return true;
    };
```

`not_indented` (Default) aligns the braces with the lambda's starting line. Both values override `csharp_indent_braces` for lambda blocks; `csharp_indent_block_contents` still controls indentation inside the braces. Indentation uses `indent_style` and `indent_size`. Missing or `unset` leaves existing indentation rules in control. This preference does not introduce line breaks or change expression lambdas or anonymous `delegate` blocks.

## Safety and file handling

DressSharp uses syntax only—never symbols, types, or semantic models. It derives language version, preprocessor symbols, source kind, and documentation mode from MSBuild. Unsupported project language versions fail before writes. Unsafe individual occurrences intersecting malformed syntax, directives, or disabled text are skipped; safe occurrences continue.

Source decoding checks a BOM, then strict UTF-8, then strict Latin-1. BOM-less UTF-16 is not guessed. Unchanged files retain their original bytes. Changed files use same-directory atomic replacement, reject changed-since-read content, and protect symlink targets.
