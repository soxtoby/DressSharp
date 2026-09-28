#!/usr/bin/env dotnet
#:package DotNetDo.Core@0.8.0
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DotNetDo;
using Serilog;

[assembly: TaskDescription("Prepare the next release version and changelog.")]

if (Do.GitRepo.IsDirty)
    throw new InvalidOperationException("prepare-release requires a clean Git worktree.");

var projectFile = Do.RootDirectory / "DressSharp/DressSharp.csproj";
var changelogFile = Do.RootDirectory / "CHANGELOG.md";
var manifestFile = Do.RootDirectory / ".config/dotnet-tools.json";
var changelog = changelogFile.ReadText().Replace("\r\n", "\n");

var current = ParseProjectVersion(projectFile);
var latest = ParseLatestRelease(changelog);
if (latest is not null && latest != current)
    throw new InvalidOperationException($"Project version {current} does not match latest changelog release {latest}.");

var unreleasedMatch = ParseUnreleased(changelog);
var unreleased = unreleasedMatch.Groups["notes"].Value;
var bump = InferBump(unreleased);
// The first release ships the version the project already declares.
var next = latest is null
    ? current
    : bump switch
        {
            Bump.Major => new Version(current.Major + 1, 0, 0),
            Bump.Minor => new Version(current.Major, current.Minor + 1, 0),
            _ => new Version(current.Major, current.Minor, current.Build + 1),
        };

// This repository formats itself with the latest published release.
if (latest is not null)
    UpdateToolPin(current.ToString(), manifestFile);

projectFile.WriteText(projectFile.ReadText().RegexReplace(
    "<Version>[^<]+</Version>",
    $"<Version>{next}</Version>"));

var before = changelog[..unreleasedMatch.Index];
var after = changelog[(unreleasedMatch.Index + unreleasedMatch.Length)..].TrimStart('\n');
var released = $"## Unreleased\n\n## v{next}\n\n{unreleased.Trim()}\n";
changelogFile.WriteText(before + released + (after.Length == 0 ? "" : "\n" + after.TrimEnd() + "\n"));

Log.Information("Next version: {Next}", next);

static Version ParseProjectVersion(AbsolutePath projectFile)
{
    var version = projectFile.ReadXml()
        .Descendants("Version")
        .Select(element => element.Value)
        .SingleOrDefault();
    return Version.TryParse(version, out var parsed)
        ? parsed
        : throw new InvalidOperationException("DressSharp.csproj has no valid Version.");
}

static Version? ParseLatestRelease(string changelog)
{
    var match = changelog.RegexMatch(@"(?m)^## v(?<version>\d+\.\d+\.\d+)\s*$");
    return match.Success ? Version.Parse(match.Groups["version"].Value) : null;
}

static Match ParseUnreleased(string changelog)
{
    var match = changelog.RegexMatch(@"(?ms)^## Unreleased\s*\n(?<notes>.*?)(?=^## |\z)");
    if (!match.Success || match.Groups["notes"].Value.IsNullOrWhiteSpace())
        throw new InvalidOperationException("CHANGELOG.md has no Unreleased notes.");
    return match;
}

static Bump InferBump(string notes)
{
    var headings = notes.RegexMatches(@"(?m)^### (?<heading>.+?)\s*$")
        .Select(match => match.Groups["heading"].Value)
        .ToArray();
    if (headings.None())
        throw new InvalidOperationException("Unreleased notes have no change headings.");

    var unknown = headings.Except(["Breaking", "Added", "Changed", "Fixed"], StringComparer.Ordinal).ToArray();
    if (unknown.Any())
        throw new InvalidOperationException($"Unknown Unreleased change heading: {unknown.JoinWith(", ")}.");

    return headings.Contains("Breaking", StringComparer.Ordinal) ? Bump.Major
        : headings.Contains("Added", StringComparer.Ordinal) || headings.Contains("Changed", StringComparer.Ordinal) ? Bump.Minor
        : Bump.Patch;
}

static void UpdateToolPin(string version, AbsolutePath manifestFile)
{
    var manifest = manifestFile.ReadJson() as JsonObject
        ?? throw new InvalidOperationException("Tool manifest has no JSON object.");
    var tool = manifest["tools"]?["dresssharp"] as JsonObject
        ?? throw new InvalidOperationException("Tool manifest has no DressSharp entry.");
    tool["version"] = version;
    manifestFile.WriteText(manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
}

enum Bump
{
    Major, Minor, Patch
}
