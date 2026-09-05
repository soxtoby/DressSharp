#!/usr/bin/env dotnet
#:package DotNetDo.Core@0.8.0
using DotNetDo;

using static DotNetDo.Tools;

[assembly:TaskDescription("Build and run the current DressSharp checkout, forwarding arguments after --.")]

var project = Do.Solution["DressSharp"];

await (Bun.Run with { Target = "build", WorkingDirectory = Do.RootDirectory / "src" / "DressSharp" / "InteractiveWeb" });

var arguments = Do.TrailingArguments.Select(argument => argument.QuotedArgument()).JoinWith(" ");
await Do.Exec($"dotnet run --project {project.Path.QuotedArgument()} -- {arguments}");
