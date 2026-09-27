---
title: Compatibility and versioning
description: Supported platforms and C# versions, and what a DressSharp version number promises.
---

## Platforms

DressSharp needs the .NET 10 SDK or later. The `DressSharp` package installs a precompiled build for your platform:

| Platform | Package |
| --- | --- |
| Windows x64 | `DressSharp.win-x64` |
| Linux x64 | `DressSharp.linux-x64` |
| macOS x64 | `DressSharp.osx-x64` |
| macOS Arm64 | `DressSharp.osx-arm64` |

Always install `DressSharp` itself, which picks the right one. Every release publishes all five packages at the same version. Other platforms aren't packaged.

## C# versions

DressSharp ships with its own copy of the Roslyn C# parser, so it doesn't depend on the compiler in your SDK or project. It accepts every released C# version that copy understands, and preview versions when a project selects them. Stable DressSharp releases ship a stable Roslyn.

## Versioning

DressSharp follows semantic versioning:

- **Major releases** may change commands, options or EditorConfig settings in ways that break existing setups.
- **Minor releases** may format some code differently. The release notes call out every such change.

Pinning a version in your tool manifest pins the formatting too: the same code and rules always give the same output.
