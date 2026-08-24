#!/usr/bin/env dotnet
#:package DotNetDo.Core@0.7.0
using DotNetDo;

[assembly: TaskDescription("Build DressSharp and use that build to format the repository.")]

var project = Do.Solution["DressSharp"];

await Do.Exec($"dotnet run --project {project.Path.QuotedArgument()}");
