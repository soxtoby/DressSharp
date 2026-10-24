# DressSharp for VS Code

Format C# with your repository's DressSharp rules, from the command palette or as VS Code's C# formatter. Requires VS Code 1.95 or later and the .NET 10 SDK or later.

The extension formats a C# file when the repository installs DressSharp as a local .NET tool, in a `.config/dotnet-tools.json` above the file, and an `.editorconfig` applies to it. Other files are left alone.

- **Format document with DressSharp** in the command palette or the editor's context menu formats the current file.
- To make **Format Document** and format on save use DressSharp for C#, run **Format Document With...**, choose **Configure Default Formatter...**, then choose DressSharp.

## Install

1. Download `DressSharpVSCode-<version>.vsix` from the **Assets** of a [DressSharp GitHub release](https://github.com/soxtoby/DressSharp/releases).
2. In VS Code, run **Extensions: Install from VSIX...** from the command palette and select the file.

See the [VS Code extension guide](https://soxtoby.github.io/DressSharp/vscode/) for repository setup and settings.
