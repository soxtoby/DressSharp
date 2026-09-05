#!/usr/bin/env dotnet
#:package DotNetDo.Core@0.8.0
using DotNetDo;
using static DotNetDo.Tools;

[assembly: TaskDescription("Build the interactive assets and .NET solution.")]

await (Bun.Run with { Target = "build", WorkingDirectory = Do.RootDirectory / "src" / "DressSharp" / "InteractiveWeb" });
await DotNet.Build;
