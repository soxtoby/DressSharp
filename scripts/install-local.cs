#!/usr/bin/env dotnet
#:package DotNetDo.Core@0.8.0
using DotNetDo;
using static DotNetDo.Tools;

[assembly: TaskDescription("Pack DressSharp and install it from the local package as a repository tool.")]

// Older versions would make selecting the wrapper package ambiguous.
var packages = (Do.RootDirectory / "dist/packages").RecreateDirectory();
var manifest = Do.RootDirectory / ".config/dotnet-tools.json";

await (Bun.Install with { FrozenLockfile = true });
await (Bun.Run with { Target = "interactive:build:production" });
await (DotNet.Pack with
    {
        Output = packages
    });

// The pack produces the wrapper package plus one package per runtime identifier; the wrapper is
// the one whose name continues with the version.
var package = packages.GlobFiles(["DressSharp.*.nupkg", "!*.symbols.nupkg"])
    .Single(candidate => char.IsDigit(candidate.Name!["DressSharp.".Length]));
var version = package.NameWithoutExtension!["DressSharp.".Length..];

if (manifest.ReadText().Contains("\"dresssharp\"", StringComparison.OrdinalIgnoreCase))
    await Do.Exec("dotnet tool uninstall DressSharp");

// A reinstall at the same version would otherwise reuse whatever the global packages folder
// already holds for that version, silently keeping the previous build.
var globalPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } configured
    ? AbsolutePath.Parse(configured)
    : AbsolutePath.Parse(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) / ".nuget" / "packages";
foreach (var cached in globalPackages.GlobDirectories("dresssharp*/" + version.ToLowerInvariant()))
    await DeleteCachedPackage(cached);
await Do.Exec(
    $"dotnet tool install DressSharp --version {version.QuotedArgument()} --add-source {packages.QuotedArgument()} --ignore-failed-sources --no-cache");

static async Task DeleteCachedPackage(AbsolutePath path)
{
    // An editor worker stops when the uninstall changes its manifest; allow it to release mapped files.
    var attempts = 0;
    while (path.IsExistingDirectory)
    {
        try
        {
            path.Delete();
        }
        catch (Exception exception) when ((exception is IOException or UnauthorizedAccessException) && attempts++ < 12)
        {
            await Task.Delay(250);
        }
    }
}
