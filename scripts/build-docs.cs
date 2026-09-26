#!/usr/bin/env dotnet
#:package DotNetDo.Core@0.8.0
using DotNetDo;
using static DotNetDo.Tools;

[assembly: TaskDescription("Generate the rule pages and build the Starlight site into artifacts/site.")]

var project = Do.Solution["tools/DressSharp.Docs"];
var website = Do.RootDirectory / "website";

await Do.Exec($"dotnet run --project {project.Path.QuotedArgument()}");
await (Bun.Install with { FrozenLockfile = true, WorkingDirectory = website });
await (Bun.Run with { Target = "build", WorkingDirectory = website });
