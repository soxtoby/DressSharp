#!/usr/bin/env dotnet
#:package DotNetDo.Core@0.8.0
using DotNetDo;
using static DotNetDo.Tools;

[assembly: TaskDescription("Generate the embedded interactive browser assets.")]

await (Bun.Install with { FrozenLockfile = true });
await (Bun.Run with { Target = "interactive:build:production" });
