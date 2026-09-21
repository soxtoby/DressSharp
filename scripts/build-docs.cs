#!/usr/bin/env dotnet
#:package DotNetDo.Core@0.8.0
using DotNetDo;
using static DotNetDo.Tools;

[assembly: TaskDescription("Generate the static documentation site into artifacts/site, forwarding arguments after --.")]

var project = Do.Solution["tools/DressSharp.Docs"];

await (Bun.Run with { Target = "build", WorkingDirectory = Do.RootDirectory / "src" / "DressSharp" / "InteractiveWeb" });

var arguments = Do.TrailingArguments.Select(argument => argument.QuotedArgument()).JoinWith(" ");
await Do.Exec($"dotnet run --project {project.Path.QuotedArgument()} -- {arguments}");
