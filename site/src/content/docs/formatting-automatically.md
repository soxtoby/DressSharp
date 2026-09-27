---
title: Formatting automatically
description: Format on every commit, after every coding agent edit, and check formatting in CI.
---

Once your rules are settled, you shouldn't have to think about formatting again. DressSharp can format just the files you've changed, so it's quick enough to run on every commit or after every edit.

Each of these setups runs the version of DressSharp in your repository's tool manifest (see [Install](../getting-started/#install)). Run `dotnet tool restore` once after cloning.

## Before each commit

A Git pre-commit hook can format the files you're committing. Save this as `.git/hooks/pre-commit`:

```sh
#!/bin/sh
dotnet dress format --staged
```

`--staged` formats only the files staged for this commit, and stages the formatted result, so the commit goes in already tidy. If you've staged only part of a file, the whole file is formatted and staged.

If you use a hook manager such as [Husky.Net](https://alirezanet.github.io/Husky.Net/) or [pre-commit](https://pre-commit.com/), give it the same command.

## After a coding agent makes changes

Coding agents rarely follow a style exactly. Run DressSharp each time an agent makes changes, and its code comes out formatted to your rules:

```sh
dotnet dress format --changed
```

`--changed` formats every file that differs from the last commit, including new files.

For Claude Code, add a hook to `.claude/settings.json`:

```json
{
  "hooks": {
    "PostToolUse": [
      {
        "matcher": "Edit|Write",
        "hooks": [{ "type": "command", "command": "dotnet dress format --changed" }]
      }
    ]
  }
}
```

For Codex, add the same hook to `.codex/hooks.json`:

```json
{
  "hooks": {
    "PostToolUse": [
      {
        "hooks": [{ "type": "command", "command": "dotnet dress format --changed" }]
      }
    ]
  }
}
```

## In CI

`dotnet dress check` changes nothing. It lists any files that don't match your rules and exits with code 1, which fails the build. In GitHub Actions:

```yaml
- uses: actions/setup-dotnet@v4
  with:
    dotnet-version: 10.0.x
- run: dotnet tool restore
- run: dotnet dress check
```

## Upgrading

A new version of DressSharp can format some code differently. The release notes say when that happens. Because the version is pinned in your tool manifest, everyone upgrades together:

```sh
dotnet tool update DressSharp
dotnet dress init
dotnet dress
```

`init` adds any rules introduced since you last ran it, each set to its default, and leaves your existing settings alone. Commit the manifest, the EditorConfig and any reformatted files together.
