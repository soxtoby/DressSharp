#!/usr/bin/env dotnet
#:package DotNetDo.Core@0.8.0
using DotNetDo;
using static DotNetDo.Tools;

[assembly: TaskDescription("Generate the embedded interactive browser assets.")]

await (Bun.Run with { Target = "build", WorkingDirectory = Do.RootDirectory / "src" / "DressSharp" / "InteractiveWeb" });
