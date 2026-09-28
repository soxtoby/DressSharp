---
title: Compatibility and versioning
description: Supported platforms and C# versions, and how to keep formatting stable.
---

## Platforms

DressSharp needs the .NET 10 SDK or later. It's precompiled for each of these platforms so that it starts faster:

- Windows x64
- Linux x64
- macOS x64
- macOS Arm64

Other platforms aren't supported.

## C# versions

A DressSharp release formats every C# version released before it, and preview features when a project enables them.

## Versioning

Pinning a version in your tool manifest pins the formatting too: the same code and rules always give the same output. Before upgrading, check the [release notes](https://github.com/soxtoby/DressSharp/releases) for any changes to formatting.
