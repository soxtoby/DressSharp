---
title: Command line
description: Every dotnet dress command and option, which files it selects, and its exit codes.
---

## Commands

| Command | What it does |
| --- | --- |
| `dotnet dress` | Same as `dotnet dress format`. |
| `dotnet dress format` | Formats the selected files and reports how many changed. |
| `dotnet dress check` | Lists the selected files that need formatting. Writes nothing. |
| `dotnet dress init` | Adds every missing rule, set to its default, to an EditorConfig file. |
| `dotnet dress interactive` | Opens the rule editor in your browser. See [Choosing your rules](../choosing-rules/). |

## Options

`format` and `check` accept these options. The bare `dotnet dress` command does too, except that `--include`, `--staged` and `--changed` must follow an explicit `format` or `check`.

| Option | Effect |
| --- | --- |
| `--include <pattern>` | Selects files matching a glob relative to the current directory, such as `src/**/*.cs` or a single file name. Repeat to add more. Quote the pattern so your shell doesn't expand it. |
| `--staged` | Selects only files whose staged content differs from `HEAD`. `format` also stages what it rewrites. |
| `--changed` | Selects only files that differ from `HEAD`, plus untracked files. |
| `--verbose` | Lists each changed file, and says so when nothing was selected. |
| `--configuration <name>` | The MSBuild configuration used to read your projects. Defaults to `Debug`. |

`--staged` and `--changed` can't be combined, and both need a Git repository. In a repository with no commits yet, they select every file.

`init` takes `--target <path>`, the EditorConfig file to write. It defaults to `.editorconfig` in the current directory, and is created if missing. A setting already present in any section is left unchanged. New settings go into the last `[*.cs]` section, or a new one at the end of the file.

`interactive` takes `--config <path>`, an EditorConfig file or the directory holding one. Without it, DressSharp uses the nearest `.editorconfig` in the current directory or above.

## Which files are formatted

Without `--include`, DressSharp selects every `.cs` file in the current directory and below. Inside a Git repository it skips anything Git ignores, the same way `git status` does. Generated files, `.csx` scripts, and symbolic links are always skipped.

DressSharp reads each file's project to learn its C# language version and preprocessor symbols, so code in `#if` blocks is handled the way the compiler sees it. A project set to a C# version DressSharp doesn't understand stops the run before anything is written.

## How files are written

- Only files whose formatting changes are rewritten. Unchanged files keep their exact bytes.
- Files are read using their byte order mark, or else as UTF-8, falling back to Latin-1. UTF-16 without a byte order mark isn't detected. The [`charset`](../rules/file/#charset) rule decides the encoding a file is written in.
- Files are replaced atomically. A file that changes on disk while DressSharp is working on it is left alone.
- Code DressSharp can't parse, and code around preprocessor directives it can't safely reformat, is left as it is. The rest of the file is still formatted.

## Exit codes

| Code | Meaning |
| ---: | --- |
| 0 | Success. For `check`, no files need formatting. |
| 1 | `check` found files that need formatting. |
| 2 | Something went wrong: an invalid option, an invalid EditorConfig value, or a file that couldn't be read or written. |
