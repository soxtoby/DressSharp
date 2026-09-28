---
title: Getting started
description: Install DressSharp, choose your rules, and format your code for the first time.
---

DressSharp only changes what your EditorConfig asks it to. With no preferences set, `dotnet dress` leaves every file as it found it. So getting started comes down to three steps: install the tool, write down your rules, then format.

## Install

DressSharp is a .NET tool and needs the .NET 10 SDK. Install it into your repository so everyone on the team, and your CI, runs the same version:

```sh
dotnet new tool-manifest
dotnet tool install DressSharp
```

Skip the first command if your repository already has a `.config/dotnet-tools.json`. Commit that file. Anyone who clones the repository then runs `dotnet tool restore` once to get the tool.

To use DressSharp everywhere on your machine instead, install it globally:

```sh
dotnet tool install --global DressSharp
```

## Write down your rules

From the root of your repository, run:

```sh
dotnet dress init
```

This adds every rule DressSharp supports to `.editorconfig`, each set to its default. It creates the file if you have none. Settings already in the file stay as they are, so the `csharp_*` and `dotnet_*` preferences your team already uses keep working.

Together the defaults make a complete, consistent style, but they're only a starting point. Open the file and you'll see your whole code style in one place, where any rule can be changed, or set to `unset` so DressSharp leaves that part of your code alone. The easiest way to change them is to try them on:

```sh
dotnet dress interactive
```

This opens your EditorConfig in the browser beside a live preview of the formatted code. See [Choosing your rules](../choosing-rules/).

## See what would change

Before touching any files, check how much of your code is already in shape:

```sh
dotnet dress check
```

`check` lists the files that don't match your rules and changes nothing. A long list is normal the first time. It usually means one or two rules differ from how your code is written today, such as `max_line_length` or `dress_namespace_style`. Adjust those rules if you'd rather keep your current style, or go ahead and format.

## Format

```sh
dotnet dress
```

This formats every C# file in the current directory and below, skipping anything Git ignores. It reports how many files changed.

Review the result with `git diff`. Formatting changes how your code is laid out, never what it does. Put the first run in a commit of its own so it's easy to review. List that commit's hash in `.git-blame-ignore-revs` and GitHub's blame view will skip it.

## Next steps

- [Choosing your rules](../choosing-rules/): find the settings that match how your team writes C#.
- [Formatting automatically](../formatting-automatically/): format on every commit, check in CI, and tidy up after coding agents.
- [Rules](../rules/): every setting, with examples of what each value does.
