#!/usr/bin/env dotnet
#:package DotNetDo.Core@0.7.0
using DotNetDo;
using static DotNetDo.Tools;

[assembly: TaskDescription("Build DressSharp and use that build to format the repository.")]

var project = Do.Solution["DressSharp"];

await (Bun.Run with { Target = "build", WorkingDirectory = Do.RootDirectory / "src" / "DressSharp" / "InteractiveWeb" });
await Do.Exec($"dotnet run --project {project.Path.QuotedArgument()}");
