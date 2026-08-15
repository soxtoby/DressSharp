#!/usr/bin/env dotnet
#:package DotNetDo.Core@0.6.0
using DotNetDo;
using static DotNetDo.Tools;

[assembly: TaskDescription("Build the solution and run tests.")]

await DotNet.Test;