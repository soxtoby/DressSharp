---
title: Rider plugin
description: Install DressSharp in Rider and format C# on save, as you type, or on demand.
---

The DressSharp plugin formats C# in Rider using your repository's EditorConfig rules. It supports format on save, format as you type, and a **Code | Format with DressSharp** action.

## Install the plugin

You need Rider 2025.2 or later and the .NET 10 SDK or later.

1. Open the [DressSharp releases on GitHub](https://github.com/soxtoby/DressSharp/releases).
2. Download `DressSharpRider-<version>.zip` from the release's **Assets**. Leave the ZIP intact.
3. In Rider, open **Settings | Plugins**, click the gear icon, then choose **Install Plugin from Disk**.
4. Select the ZIP and restart Rider if prompted.

## Set up your repository

The plugin formats a C# file when both of these apply:

- DressSharp is installed as a **local .NET tool** in the file's directory or a parent directory. A global installation alone doesn't enable the plugin.
- An **`.editorconfig`** in the file's directory or a parent directory defines the formatting preferences you want to apply.

From the repository root, install the current release of DressSharp and initialize your preferences:

```sh
dotnet tool install DressSharp
dotnet dress init
```

Installation creates a tool manifest if needed. If DressSharp is already listed in it, run `dotnet tool restore` after cloning instead of installing it again. To update an existing installation, run `dotnet tool update DressSharp`.

Commit the tool manifest and `.editorconfig` so everyone uses the same formatter and rules. `init` adds missing preferences at their defaults and keeps your existing choices. See [Choosing your rules](../choosing-rules/) to customize them.

DressSharp only changes what your EditorConfig asks it to. Preferences that are missing or set to `unset` leave that part of your code alone. Files without a local tool installation or an applicable EditorConfig are left alone too.

## Format your code

- **On save:** save an eligible C# file to format it. This is enabled by default.
- **As you type:** enable the typing option in **Settings | Tools | DressSharp**. Typing `;` formats the statement just finished; typing `}` formats the block just finished. The rest of the file stays as you left it. Formatting applies to whole lines, so statements sharing a line are formatted together.
- **On demand:** choose **Code | Format with DressSharp** for the current file, or select C# files in the Solution Explorer and run the same action.

Save and manual formatting skip generated files and files Git ignores. If a file isn't being formatted, the manual action tells you why.

Typing leaves a change alone if it would also alter lines outside the statement or block you just finished. Use **Code | Format with DressSharp** to bring an existing file into line with your rules first.

## Settings

Open **Settings | Tools | DressSharp**.

| Setting | Default | What it does |
| --- | --- | --- |
| **Format C# files with DressSharp on save** | On | Formats eligible C# files whenever Rider saves them. You can also control this from **Settings \| Tools \| Actions on Save**. |
| **Format the statement or block just finished when you type `;` or `}`** | Off | Formats the lines you just completed while you type. |
| **Keep a formatter process running in the background** | On | Keeps DressSharp ready between save, typing, and manual formatting runs. Uses more memory while the Rider project is open. Turn it off to start a fresh formatter for each run. |
| **dotnet executable** | Automatic | Leave empty for Rider to find `dotnet`, or select the executable if your .NET installation isn't found. |

The three formatting options are stored with the solution. The `dotnet` executable setting applies to this machine.

If the repository's DressSharp version doesn't support background formatting, update it with `dotnet tool update DressSharp` or turn off the background option. Save and manual formatting still work with older tool versions.
