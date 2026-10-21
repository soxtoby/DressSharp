#!/usr/bin/env dotnet
#:package DotNetDo.Core@0.9.0
using DotNetDo;
using static DotNetDo.Tools;

[assembly: TaskDescription("Build the solution and run tests.")]

await (Bun.Install with { FrozenLockfile = true });
await (Bun.Run with { Target = "interactive:check" });
// The interactive server tests serve the embedded assets, so a clean checkout needs them built.
await (Bun.Run with { Target = "interactive:build" });
// The test projects reference Microsoft.Testing.Extensions.GitHubActionsReport, which annotates failures
// and writes the step summary on GitHub Actions.
await (DotNet.Test with
    {
        TestingPlatform = new() { ReportGitHub = Do.GitHubActions is not null }
    });
