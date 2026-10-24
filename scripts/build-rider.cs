#!/usr/bin/env dotnet
#:package DotNetDo.Core@0.8.0
using DotNetDo;

[assembly: TaskDescription("Build and test the Rider plugin, and copy its ZIP into dist.")]

var riderPath = Do.Param("rider-path").Value;
var plugin = Do.RootDirectory / "extensions/DressSharpRider";
var wrapper = OperatingSystem.IsWindows()
    ? (plugin / "gradlew.bat").QuotedArgument()
    : $"sh {(plugin / "gradlew").QuotedArgument()}";
var sdk = riderPath is null ? "" : $" -PriderPath={riderPath.QuotedArgument()}";

await Do.Exec($"{wrapper} --console=plain build buildPlugin{sdk}", new() { WorkingDirectory = plugin });

var version = (Do.RootDirectory / "DressSharp/DressSharp.csproj").ReadXml()
    .Descendants("Version")
    .Select(element => element.Value)
    .Single();
var packageName = $"DressSharpRider-{version}.zip";
var distribution = (Do.RootDirectory / "dist").EnsureDirectoryExists() / packageName;
(plugin / "build/distributions" / packageName).CopyTo(distribution, new() { Overwrite = true });
Console.WriteLine(distribution);
