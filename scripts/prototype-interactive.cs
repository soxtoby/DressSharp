#!/usr/bin/env dotnet
#:package DotNetDo.Core@0.7.0
using DotNetDo;

[assembly: TaskDescription("Run the throwaway interactive web-page prototype.")]

var prototype = Do.RootDirectory / "prototypes/interactive-web";
await Do.Exec($"bun run --cwd {prototype.QuotedArgument()} dev");
