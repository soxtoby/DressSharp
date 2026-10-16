# DressSharp for Rider

A JetBrains Rider plugin that formats C# files with [DressSharp](https://github.com/soxtoby/DressSharp), the unopinionated formatter that applies only the rules you set in EditorConfig.

A file is formatted when both of these hold:

- The repository installs DressSharp as a **local .NET tool**: a `.config/dotnet-tools.json` (or `dotnet-tools.json`) in the file's directory or above lists the `dresssharp` package. Manifests are found the way the dotnet CLI finds them, so a manifest marked `isRoot` ends the search.
- An **`.editorconfig` is in scope**: one exists in the file's directory or above.

Any other file is left alone, so the plugin is safe to leave enabled across every solution. A global `dotnet tool install -g DressSharp` on its own is deliberately not enough, so a repository only gets formatted when it has opted in.

## What it does

- **Format on save.** Switched on by default, and controlled from Settings | Tools | Actions on Save, or Settings | Tools | DressSharp. The setting is stored with the solution.
- **Code | Format with DressSharp.** Formats the current file, or the C# files selected in the Solution Explorer. When a file isn't set up for DressSharp, this tells you why.

Each run invokes `dotnet dress format --verbose --include <file>` from the manifest's directory, with one process per manifest and the files from a save batched together. When DressSharp rewrites a file, the editor reloads it from disk. Errors from DressSharp or dotnet are shown as notifications. When the tool is in the manifest but hasn't been restored on this machine, the notification offers to run `dotnet tool restore`.

Since DressSharp formats files on disk rather than through standard input, the plugin saves a file before formatting it. If you type into a file between saving it and DressSharp finishing, Rider shows its usual file-changed-on-disk prompt.

## Settings

Settings | Tools | DressSharp has:

- **Format C# files with DressSharp on save**, per solution.
- **dotnet executable**, per machine. Leave empty to use the `dotnet` on the PATH, falling back to `DOTNET_ROOT` and the usual SDK install locations.

## Building

The build needs a JDK 21 or later; Rider's bundled runtime works. Building against an installed Rider avoids downloading one:

```sh
cd extensions/DressSharpRider
export JAVA_HOME="C:/Users/me/AppData/Local/Programs/Rider/jbr"
sh ./gradlew build buildPlugin -PriderPath="C:/Users/me/AppData/Local/Programs/Rider"
```

Without `riderPath`, the build downloads the Rider release named by `platformVersion` in `gradle.properties`. The plugin zip is written to `build/distributions/`. Install it from Settings | Plugins | ⚙ | Install Plugin from Disk.

On Windows, set `JAVA_HOME` to Rider's `jbr` directory and use `gradlew.bat` in place of `sh ./gradlew`.

`sh ./gradlew runIde -PriderPath=...` starts a Rider with the plugin loaded.

## Compatibility

Rider 2025.2 and later. The plugin is pure JVM and uses only platform APIs, so it has no ReSharper backend component.
