#!/usr/bin/env dotnet
#:package DotNetDo.Core@0.8.0
using DotNetDo;
using static DotNetDo.Tools;

[assembly: TaskDescription("Build the solution and run tests.")]

await (Bun.Install with { FrozenLockfile = true });
await (Bun.Run with { Target = "interactive:check" });
// The interactive server tests serve the embedded assets, so a clean checkout needs them built.
await (Bun.Run with { Target = "interactive:build" });
await DotNet.Test;
