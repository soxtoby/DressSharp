#!/usr/bin/env dotnet
#:package DotNetDo.Core@0.8.0
#:project ../DressSharp/DressSharp.csproj
#:include docs/ContentWriter.cs
#:include docs/LineDiff.cs
#:include docs/RuleDocumentation.cs
#:property AssemblyName=DressSharp.Docs
using System.Reflection;
using DotNetDo;
using DressSharp.Docs;
using static DotNetDo.Tools;

[assembly: TaskDescription("Generate the rule pages and build the Starlight site into dist/site.")]

var version = typeof(DressSharp.Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? typeof(DressSharp.Program).Assembly.GetName().Version?.ToString()
    ?? "0.0.0";
version = version.Split('+')[0];

var groups = await CatalogReader.ReadAsync(default);
await new ContentWriter(Do.RootDirectory, version).WriteAsync(groups, default);
var rules = groups.Sum(group => group.Rules.Count());
var outcomes = groups.Sum(group => group.Rules.Sum(rule => rule.Outcomes.Length));
Console.WriteLine($"Wrote {rules} rules with {outcomes} formatted examples");

await (Bun.Install with { FrozenLockfile = true });
await (Bun.Run with { Target = "site:build" });
