# DressSharp for Rider

A JetBrains Rider plugin that formats C# files with [DressSharp](https://github.com/soxtoby/DressSharp), the unopinionated formatter that applies only the rules you set in EditorConfig.

A file is formatted when both of these hold:

- The repository installs DressSharp as a **local .NET tool**: a `.config/dotnet-tools.json` (or `dotnet-tools.json`) in the file's directory or above lists the `dresssharp` package. Manifests are found the way the dotnet CLI finds them, so a manifest marked `isRoot` ends the search.
- An **`.editorconfig` is in scope**: one exists in the file's directory or above.

Any other file is left alone, so the plugin is safe to leave enabled across every solution. A global `dotnet tool install -g DressSharp` on its own is deliberately not enough, so a repository only gets formatted when it has opted in.

## What it does

- **Format on save.** Switched on by default, and controlled from Settings | Tools | Actions on Save, or Settings | Tools | DressSharp. The setting is stored with the solution.
- **Code | Format with DressSharp.** Formats the current file, or the C# files selected in the Solution Explorer. When a file isn't set up for DressSharp, this tells you why.
- **Format as you type**, experimental and off by default. Typing `;` or `}` in an eligible file formats the statement or block just finished. See [below](#format-as-you-type).

Each run invokes `dotnet dress format --verbose --include <file>` from the manifest's directory, with one process per manifest and the files from a save batched together. When DressSharp rewrites a file, the editor reloads it from disk. Errors from DressSharp or dotnet are shown as notifications. When the tool is in the manifest but hasn't been restored on this machine, the notification offers to run `dotnet tool restore`.

Since DressSharp formats files on disk rather than through standard input, the plugin saves a file before formatting it. If you type into a file between saving it and DressSharp finishing, Rider shows its usual file-changed-on-disk prompt.

## Format as you type

Switched on from Settings | Tools | DressSharp, and stored with the solution. When you type `;` or `}` in an eligible C# file, the plugin waits a moment for Rider's own typing assists to finish, then pipes the unsaved document through `dotnet dress format --stdin <file>`, which formats it as the file at that path without reading or writing the file on disk. Of the changes DressSharp would make to the whole file, only those on the lines of the statement or block just finished are applied: after `;`, back to the previous `;`, `{`, or `}`; after `}`, back to its matching `{`. Those boundaries are found by looking at the characters rather than the syntax, so braces in strings or comments can widen or narrow the range. The rest of the file is left as you had it, so a file that has never been formatted doesn't jump when you type.

The result is applied only when the document hasn't changed since it was sent. Typing another `;` or `}` before DressSharp answers starts over with the newer text.

Each keystroke costs a `dotnet dress` process. With a warm MSBuild cache that takes around a quarter of a second on a fast machine; the first run after a project change takes longer while the project is evaluated. Errors are shown once per distinct message rather than on every keystroke.

This is a prototype of how such a feature should behave. What it doesn't yet do: it hasn't been verified against every way Rider sends C# keystrokes to its backend, so if typing `;` never formats anything, check `idea.log` with debug logging enabled for `com.soxtoby.dresssharp.rider.DressSharpTypingFormatter`; it reformats whole lines, so a statement that shares a line with another is formatted with it; and it waits for a process per keystroke rather than keeping DressSharp running.

## Settings

Settings | Tools | DressSharp has:

- **Format C# files with DressSharp on save**, per solution.
- **Format the statement or block just finished when you type `;` or `}`**, per solution and off by default.
- **dotnet executable**, per machine. Leave empty to use the `dotnet` on the PATH, falling back to `DOTNET_ROOT` and the usual SDK install locations.

## Install

Download `DressSharpRider-<version>.zip` from a [DressSharp GitHub release](https://github.com/soxtoby/DressSharp/releases). In Rider, open Settings | Plugins | ⚙ | Install Plugin from Disk and select the ZIP without unpacking it.

For a development build, open the [Rider plugin workflow](https://github.com/soxtoby/DressSharp/actions/workflows/rider.yml), choose a successful run, and download its plugin ZIP artifact. GitHub requires you to sign in to download workflow artifacts, and they expire according to the repository's retention settings.

## Building

The pinned Rider SDK needs JDK 25; that Rider version's bundled runtime works. Building against an installed Rider avoids downloading one:

```sh
cd extensions/DressSharpRider
export JAVA_HOME="C:/Users/me/AppData/Local/Programs/Rider/jbr"
sh ./gradlew build buildPlugin -PriderPath="C:/Users/me/AppData/Local/Programs/Rider"
```

Without `riderPath`, the build downloads the Rider release named by `platformVersion` in `gradle.properties`. The plugin version comes from `DressSharp/DressSharp.csproj`, and the plugin ZIP is written to `build/distributions/`.

On Windows, set `JAVA_HOME` to Rider's `jbr` directory and use `gradlew.bat` in place of `sh ./gradlew`.

`sh ./gradlew runIde -PriderPath=...` starts a Rider with the plugin loaded.

## Releasing

The formatter and editor extensions share one version, changelog, and release cycle. Add release notes under **Unreleased** in the root `CHANGELOG.md`; the plugin links to those same notes.

From a clean worktree, run `./do prepare-release` at the repository root, review and commit the version and changelog changes, and push the matching `v<version>` tag. The **Release** workflow builds and tests the formatter and Rider plugin, then publishes one GitHub Release containing the NuGet packages and plugin ZIP. The plugin is included even when only the formatter changed.

The shared release version does not require a repository to install the same DressSharp tool version as its Rider plugin.

Pull requests and pushes to `master` build the plugin when its directory, the shared project version, or a release workflow changes. **Run workflow** creates a downloadable development build without publishing a release. For a local release, build the plugin and run `./do pack` before `./do release`.

## Compatibility

Rider 2025.2 and later. The plugin is pure JVM and uses only platform APIs, so it has no ReSharper backend component.
