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

Select lines in either preview pane, then click **Show related rules** in the permanent preview-selection row beneath preference search to filter preferences to settings affecting those lines. The selected pane keeps a gutter marker and the results stay pinned while you try different settings. Click **Show related rules** again to recheck; the **×** clear button, selecting different lines, or editing source clears the filter. The check temporarily disables each active setting in separate in-memory previews. It identifies individual settings affecting the current output, so already-satisfied settings or settings masked by another rule may not appear. Selection is measured by whole lines, including any lines introduced by wrapping. No configuration changes are saved by this check.

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

### Initializer layout

Object, collection, array, and `with` initializers have independent layout preferences:

```editorconfig
dress_object_initializer_layout = auto
dress_collection_initializer_layout = auto
dress_array_initializer_layout = auto
dress_with_initializer_layout = auto
```

Each accepts `compact`, `auto` (Default), or `expanded`. `compact` uses one line when the result does not exceed `max_line_length`, otherwise it expands. `auto` keeps a fitting single-line initializer on one line and normalizes any multiline or oversized initializer to expanded form. `expanded` always puts the opening brace, every item, and the closing brace on separate lines. Empty initializers have no items but follow the configured brace layout.

These preferences own the complete layout of their initializer kind. When configured, they override `csharp_new_line_before_open_brace` and `csharp_new_line_before_members_in_object_initializers` at the same boundaries. The corresponding `dress_*_initializer_indentation` preference controls indentation after a multiline layout is selected. Missing or `unset` leaves the standard newline preferences in control.

### Nested ternaries and operator position

```editorconfig
dress_conditional_expressions_layout = auto
dress_nested_conditional_style = flat
dotnet_style_operator_placement_when_wrapping = beginning_of_line
```

Wrapped binary expressions have an independent indentation preference:

```editorconfig
dress_binary_expression_indentation = flat
```

It accepts `flat` (Default) or `precedence`. `flat` aligns every wrapped binary operator. `precedence` indents a nested higher-precedence binary group by one additional level. It controls indentation only; `dress_binary_expressions_layout` decides whether the expression wraps.

```csharp
var result = first
    || second
        && third;
```

`dress_nested_conditional_style` accepts `flat` (Default), `staircase`, and `decision_ladder`. The existing conditional layout preference controls whether expressions wrap. The style preference alone reshapes already-wrapped expressions; single-line expressions stay single-line. `always_single` takes precedence over the style.

Flat keeps ternary operators at one indentation level. Staircase adds one level per nested ternary. Decision ladder puts each condition/result pair on one line, flattening only false-branch chains:

```csharp
var result =
    first ? one
    : second ? two
    : three;
```

Decision ladders fall back to staircase when a condition or branch remains multiline, including multiline token content and wrapping introduced by another layout rule. True-branch nesting also uses staircase. There is no separate fallback preference.

The standard `dotnet_style_operator_placement_when_wrapping` accepts `beginning_of_line` (Default) and `end_of_line`. It applies to binary operators and ternary `?` / `:`. With `end_of_line`, the ladder above becomes:

```csharp
var result =
    first ? one :
    second ? two :
    three;
```

Operator position alone moves existing operator breaks without introducing new ones. Commented operator boundaries are preserved. Missing or `unset` style/position preferences leave the existing layout behavior in control.

### Collection spread spacing

Use `dress_space_after_collection_spread_operator = true` (Default) for `[.. values]`, or `false` for `[..values]`. It applies only to collection-expression spread elements, not range expressions such as `values[..end]`. Missing or `unset` preserves existing spacing.

### Collection expression indentation

Collection expressions (`[...]`) have separate indentation preferences for arguments and other contexts:

```editorconfig
dress_collection_expression_indentation = indented
dress_collection_expression_argument_indentation = not_indented
```

These are the Defaults. Both accept `indented` and `not_indented`. The first applies outside arguments, including field/property initializers, variable declarations, assignments, and returns. The second applies when the collection expression is an argument to a call, constructor, or indexer, including named arguments and expressions wrapped in parentheses or casts. A collection returned by a lambda inside an argument uses the first rule.

The argument preference controls the extra collection indentation relative to its surrounding argument indentation. Neither preference introduces line breaks; `csharp_indent_block_contents` controls element indentation. Missing or `unset` adds no indentation opinion. Previously, `dress_collection_expression_indentation` also applied to arguments; configure both keys to preserve that behavior.

### Switch expression indentation

Use `dress_switch_expression_indentation = indented` (Default) to place multiline switch expression braces one indentation level below the line containing `switch`:

```csharp
var result = value switch
    {
        true => 1,
        false => 0
    };
```

`not_indented` aligns the braces with the line containing `switch`. Both values override `csharp_indent_braces` for switch expressions; `csharp_indent_block_contents` controls the arms. Missing or `unset` leaves existing indentation rules in control. This preference does not introduce line breaks or change switch statements.

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
