#!/usr/bin/env dotnet
#:package DotNetDo.Core@0.8.0
using DotNetDo;
using static DotNetDo.Tools;

[assembly: TaskDescription("Pack the DressSharp tool packages into dist/packages.")]

// The release publishes everything here, so a stale package from an earlier version must not survive.
var packages = (Do.RootDirectory / "dist/packages").RecreateDirectory();

await (Bun.Install with { FrozenLockfile = true });
await (Bun.Run with { Target = "interactive:build:production" });
await (DotNet.Pack with
    {
        Targets = [Do.Solution["DressSharp"]],
        Output = packages
    });
