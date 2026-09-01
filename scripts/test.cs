#!/usr/bin/env dotnet
#:package DotNetDo.Core@0.7.0
using DotNetDo;
using static DotNetDo.Tools;

[assembly: TaskDescription("Build the solution and run tests.")]

await (Bun.Run with { Target = "check", WorkingDirectory = Do.RootDirectory / "src" / "DressSharp" / "InteractiveWeb" });
await (Bun.Run with { Target = "build", WorkingDirectory = Do.RootDirectory / "src" / "DressSharp" / "InteractiveWeb" });
await DotNet.Test;
