#!/usr/bin/env dotnet
#:package DotNetDo.Core@0.8.0
using System.Text.RegularExpressions;
using DotNetDo;
using static DotNetDo.Tools;

[assembly: TaskDescription("Publish the packed NuGet packages and the GitHub release.")]

var tag = Do.GitHubActions?.Workflow.ReferenceName is { } defaultTag
    ? Do.Param("tag", defaultTag, "Release tag.").Value
    : Do.Param("tag").Required().Value;
var apiKey = Do.Secret("nuget_api_key", null, "Temporary NuGet API key.").Required();

var version = (Do.RootDirectory / "DressSharp/DressSharp.csproj").ReadXml()
    .Descendants("Version")
    .Select(element => element.Value)
    .SingleOrDefault()
    ?? throw new InvalidOperationException("DressSharp.csproj has no Version.");

var expectedTag = "v" + version;
if (tag != expectedTag)
    throw new InvalidOperationException($"Tag '{tag}' does not match project version '{expectedTag}'.");

var changelog = (Do.RootDirectory / "CHANGELOG.md").ReadText().Replace("\r\n", "\n");
var notesMatch = changelog.RegexMatch($@"(?ms)^## {Regex.Escape(tag)}\s*\n(?<notes>.*?)(?=^## |\z)");
if (!notesMatch.Success || notesMatch.Groups["notes"].Value.IsNullOrWhiteSpace())
    throw new InvalidOperationException($"CHANGELOG.md has no release notes for {tag}.");

// The tool package names one package per runtime identifier, and every release publishes all of them
// at the same version.
string[] runtimes = ["win-x64", "linux-x64", "osx-x64", "osx-arm64"];
var packages = Do.RootDirectory / "dist/packages";
var expected = runtimes.Select(runtime => $"DressSharp.{runtime}.{version}.nupkg")
    .Append($"DressSharp.{version}.nupkg")
    .ToArray();
var packageFiles = packages.GlobFiles("*.nupkg");
var names = packageFiles.Select(package => package.Name!).ToArray();
if (!names.Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal)))
    throw new InvalidOperationException($"Expected {expected.JoinWith(", ")} but found {names.JoinWith(", ")}.");

// Push the tool package last, so it never resolves to a runtime package that isn't there yet.
foreach (var name in expected)
{
    await (DotNet.NuGetPush with
        {
            Package = packages / name,
            ApiKey = apiKey.Unwrap(),
            Source = "https://api.nuget.org/v3/index.json",
            SkipDuplicate = true,
        });
}

var notesFile = Do.CreateTempFile("dresssharp-release-notes-", ".md");
notesFile.WriteText(notesMatch.Groups["notes"].Value.Trim() + Environment.NewLine);
var assets = expected.Select(name => (packages / name).QuotedArgument()).JoinWith(" ");
await Do.Exec($"gh release create {tag.QuotedArgument()} {assets} --title {tag.QuotedArgument()} --notes-file {notesFile.QuotedArgument()}");
