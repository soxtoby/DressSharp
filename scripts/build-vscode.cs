#!/usr/bin/env dotnet
#:package DotNetDo.Core@0.9.0
using DotNetDo;
using static DotNetDo.Tools;

[assembly: TaskDescription("Test the VS Code extension and package its VSIX into dist.")]

await (Bun.Install with { FrozenLockfile = true });
await (Bun.Run with { Target = "vscode:check" });
// The VSIX takes the formatter's version from DressSharp.csproj.
await (Bun.Run with { Target = "vscode:package" });
