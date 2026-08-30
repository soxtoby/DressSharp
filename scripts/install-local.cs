#!/usr/bin/env dotnet
#:package DotNetDo.Core@0.7.0
using DotNetDo;
using static DotNetDo.Tools;

[assembly: TaskDescription("Pack DressSharp and install it from the local package as a repository tool.")]

var packages = (Do.RootDirectory / "artifacts/packages").EnsureDirectoryExists();
var manifest = Do.RootDirectory / ".config/dotnet-tools.json";

await (Bun.Run with { Target = "build", WorkingDirectory = Do.RootDirectory / "src" / "DressSharp" / "InteractiveWeb" });
await (DotNet.Pack with
    {
        Output = packages
    });

var package = packages.GlobFiles(["DressSharp.*.nupkg", "!*.symbols.nupkg"]).Single();
var version = package.NameWithoutExtension!["DressSharp.".Length..];

if (manifest.ReadText().Contains("\"dresssharp\"", StringComparison.OrdinalIgnoreCase))
    await Do.Exec("dotnet tool uninstall DressSharp");

await Do.Exec($"dotnet tool install DressSharp --version {version.QuotedArgument()} --add-source {packages.QuotedArgument()} --ignore-failed-sources --no-cache");
