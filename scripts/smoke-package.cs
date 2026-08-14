#!/usr/bin/env dotnet
#:package DotNetDo.Core@0.6.0
using DotNetDo;
using static DotNetDo.Tools;

[assembly: TaskDescription("Pack DressSharp, install it as local and tool-path tools, and verify both commands.")]

var configuration = Do.Param("configuration", "Release", "Build configuration.").Value;
var root = Do.RootDirectory;
var scratch = Do.CreateTempDirectory("DressSharp-smoke-");
var packages = (scratch / "packages").EnsureDirectoryExists();
var globalTools = (scratch / "global-tools").EnsureDirectoryExists();
var localTools = (scratch / "local-tools").EnsureDirectoryExists();

try
{
    await (DotNet.Pack with
        {
            Targets = [root / "src/DressSharp/DressSharp.csproj"],
            Configuration = configuration,
            Output = packages
        });

    var package = packages.GlobFiles("DressSharp.*.nupkg").Single(path => !path.Name!.EndsWith(".symbols.nupkg", StringComparison.Ordinal));
    var version = package.NameWithoutExtension!["DressSharp.".Length..];
    await Do.Exec($"dotnet tool install DressSharp --tool-path {globalTools.QuotedArgument()} --version {version.QuotedArgument()} --add-source {packages.QuotedArgument()} --ignore-failed-sources");
    await Do.Exec($"{(globalTools / (OperatingSystem.IsWindows() ? "dotnet-dress.exe" : "dotnet-dress")).QuotedArgument()} --version");
    await Do.Exec("dotnet new tool-manifest", new() { WorkingDirectory = localTools });
    await Do.Exec($"dotnet tool install DressSharp --version {version.QuotedArgument()} --add-source {packages.QuotedArgument()} --ignore-failed-sources", new() { WorkingDirectory = localTools });
    await Do.Exec("dotnet tool run dotnet-dress --version", new() { WorkingDirectory = localTools });
}
finally
{
    scratch.Delete();
}
