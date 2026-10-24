---
title: VS Code extension
description: Install DressSharp in VS Code and format C# from the command palette, with Format Document, or on save.
---

The DressSharp extension formats C# in VS Code using your repository's EditorConfig rules. Run it from the command palette, or make it VS Code's C# formatter so that **Format Document** and format on save use it.

## Install the extension

You need VS Code 1.95 or later and the .NET 10 SDK or later.

1. Open the [DressSharp releases on GitHub](https://github.com/soxtoby/DressSharp/releases).
2. Download `DressSharpVSCode-<version>.vsix` from the release's **Assets**.
3. In VS Code, run **Extensions: Install from VSIX...** from the command palette, then select the file.

## Set up your repository

The extension formats a C# file when both of these apply:

- DressSharp 0.6.0 or later is installed as a **local .NET tool** in the file's directory or a parent directory. A global installation alone doesn't enable the extension.
- An **`.editorconfig`** in the file's directory or a parent directory defines the formatting preferences you want to apply.

From the repository root, install the current release of DressSharp and initialize your preferences:

```sh
dotnet tool install DressSharp
dotnet dress init
```

Installation creates a tool manifest if needed. If DressSharp is already listed in it, run `dotnet tool restore` after cloning instead of installing it again. To update an existing installation, run `dotnet tool update DressSharp`.

Commit the tool manifest and `.editorconfig` so everyone uses the same formatter and rules. See [Choosing your rules](../choosing-rules/) to customize them.

The extension runs only in trusted workspaces, since it runs the repository's DressSharp tool and reads its projects.

## Format your code

- **From the command palette:** run **Format document with DressSharp**, or choose it from the editor's context menu. It formats the current file whichever formatter is VS Code's default.
- **With Format Document:** run **Format Document With...** from the command palette or the editor's context menu, choose **Configure Default Formatter...**, then choose DressSharp. VS Code saves the choice in your user settings, and **Format Document** then uses DressSharp for C# files. If DressSharp is your only C# formatter, VS Code uses it without asking.
- **On save:** once DressSharp is the default formatter, turn on `editor.formatOnSave`.

To set both for one repository, so they apply to everyone who opens it, add these to its `.vscode/settings.json`:

```json
"[csharp]": {
  "editor.defaultFormatter": "soxtoby.dresssharp",
  "editor.formatOnSave": true
}
```

DressSharp formats whole documents, including unsaved changes. The first format in a repository takes a moment while DressSharp starts and reads the file's project; later ones are quick.

If a file isn't being formatted, **Format document with DressSharp** tells you why. **Format Document** and format on save show a short message in the status bar instead, and the **DressSharp** channel in the **Output** panel records each run.

## Settings

| Setting | Default | What it does |
| --- | --- | --- |
| `dresssharp.backgroundProcess` | On | Keeps a DressSharp process running for each repository, so formatting doesn't wait for one to start. Uses more memory while VS Code is open. Turn it off to start a fresh process for each format. |
| `dresssharp.dotnetPath` | Empty | Leave empty for the extension to find `dotnet`, or set the path of the executable if your .NET installation isn't found. Only your user settings can set it. |

The background process restarts when you change these settings or update the repository's DressSharp version.

If the extension reports that the repository's DressSharp version can't format in VS Code, update it with `dotnet tool update DressSharp`.
