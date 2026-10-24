# DressSharp for Rider

A JetBrains Rider plugin that formats C# files with [DressSharp](https://github.com/soxtoby/DressSharp), the unopinionated formatter that applies only the rules you set in EditorConfig.

A file is formatted when both of these hold:

- The repository installs DressSharp as a **local .NET tool**: a `.config/dotnet-tools.json` (or `dotnet-tools.json`) in the file's directory or above lists the `dresssharp` package. Manifests are found the way the dotnet CLI finds them, so a manifest marked `isRoot` ends the search.
- An **`.editorconfig` is in scope**: one exists in the file's directory or above.

Any other file is left alone, so the plugin is safe to leave enabled across every solution. A global `dotnet tool install -g DressSharp` on its own is deliberately not enough, so a repository only gets formatted when it has opted in.

## What it does

- **Format on save.** Switched on by default, and controlled from Settings | Tools | Actions on Save, or Settings | Tools | DressSharp. The setting is stored with the solution.
- **Code | Format with DressSharp.** Formats the current file, or the C# files selected in the Solution Explorer. When a file isn't set up for DressSharp, this tells you why.
- **Format as you type.** Off by default. Typing `;` or `}` in an eligible file formats the statement or block just finished. See [below](#format-as-you-type).

By default, save, typing, and manual formatting share one background formatter per repository. It starts on demand from the tool manifest's directory and stays alive until the Rider project closes. Each request reads the current preferences and project context. Tool manifest, formatter, or SDK configuration changes stop the process; the next request starts it again. The first request can take longer while the runtime starts and the project is evaluated.

Turn off **Keep a formatter process running in the background** in Settings | Tools | DressSharp to start a fresh process for each run instead. Applying this setting lets current requests finish, then stops any background formatters. The setting is stored with the solution. Background formatting requires a formatter build with this support; for a development build, run `./do install-local`, then install the plugin ZIP built by `./do build-rider`.

Save and manual formatting batch files from the same repository and format them on disk, preserving DressSharp's file selection and encoding preferences. With the background process disabled, each batch invokes `dotnet dress format --verbose --include <file>`. When DressSharp rewrites a file, the editor reloads it from disk. Errors from DressSharp or dotnet are shown as notifications. When the tool is in the manifest but hasn't been restored on this machine, the notification offers to run `dotnet tool restore`.

The plugin saves a file before save or manual formatting. If you type into a file between saving it and DressSharp finishing, Rider shows its usual file-changed-on-disk prompt.

## Format as you type

Switched on from Settings | Tools | DressSharp, and stored with the solution. When you type `;` or `}` in an eligible C# file, the plugin waits a moment for Rider's own typing assists to finish, then sends the unsaved document to DressSharp without reading or writing the file on disk. It uses the shared background process when enabled, or `dotnet dress format --stdin <file>` in a fresh process when disabled. Of the changes DressSharp would make to the whole file, only those on the lines of the statement or block just finished are applied: after `;`, back to the previous `;`, `{`, or `}`; after `}`, back to its matching `{`. Those boundaries are found by looking at the characters rather than the syntax, so braces in strings or comments can widen or narrow the range. The rest of the file is left as you had it, so a file that has never been formatted doesn't jump when you type.

The result is applied only when the document hasn't changed since it was sent. Typing another `;` or `}` before DressSharp answers starts over with the newer text.

Typing requires a formatter build with standard-input support. Errors are shown once per distinct message rather than on every keystroke.

Formatting applies to whole lines, so a statement that shares a line with another is formatted with it. If typing `;` never formats anything, check `idea.log` with debug logging enabled for `com.soxtoby.dresssharp.rider.DressSharpTypingFormatter`.

## Settings

Settings | Tools | DressSharp has:

- **Format C# files with DressSharp on save**, per solution.
- **Format the statement or block just finished when you type `;` or `}`**, per solution and off by default.
- **Keep a formatter process running in the background**, per solution and on by default. Applies to save, typing, and manual formatting.
- **dotnet executable**, per machine. Leave empty to use the `dotnet` on the PATH, falling back to `DOTNET_ROOT` and the usual SDK install locations.

## Install

Download `DressSharpRider-<version>.zip` from a [DressSharp GitHub release](https://github.com/soxtoby/DressSharp/releases). In Rider, open Settings | Plugins | ⚙ | Install Plugin from Disk and select the ZIP without unpacking it.

For a development build, open the [Rider plugin workflow](https://github.com/soxtoby/DressSharp/actions/workflows/rider.yml), choose a successful run, and download its plugin ZIP artifact. GitHub requires you to sign in to download workflow artifacts, and they expire according to the repository's retention settings.

## Building

From the repository root, run `./do build-rider` to build and test the plugin and write `dist/DressSharpRider-<version>.zip`. The plugin version comes from `DressSharp/DressSharp.csproj`.

The pinned Rider SDK needs JDK 25; that Rider version's bundled runtime works. Building against an installed Rider avoids downloading one:

```sh
export JAVA_HOME="C:/Users/me/AppData/Local/Programs/Rider/jbr"
./do build-rider --rider-path="C:/Users/me/AppData/Local/Programs/Rider"
```

Without `--rider-path` or `DRESSSHARP_RIDER_PATH`, the build downloads the Rider release named by `platformVersion` in `gradle.properties`. Gradle also keeps the plugin ZIP in `extensions/DressSharpRider/build/distributions/`.

On Windows, set `JAVA_HOME` to Rider's `jbr` directory and run `./do build-rider`; the task uses `gradlew.bat` automatically.

From `extensions/DressSharpRider`, `sh ./gradlew runIde -PriderPath=...` starts a Rider with the plugin loaded.

## Releasing

The formatter and editor extensions share one version, changelog, and release cycle. Add release notes under **Unreleased** in the root `CHANGELOG.md`; the plugin links to those same notes.

From a clean worktree, run `./do prepare-release` at the repository root, review and commit the version and changelog changes, and push the matching `v<version>` tag. The **Release** workflow builds and tests the formatter and Rider plugin, then publishes one GitHub Release containing the NuGet packages and plugin ZIP. The plugin is included even when only the formatter changed.

The shared release version does not require a repository to install the same DressSharp tool version as its Rider plugin.

Pull requests and pushes to `master` build the plugin when its directory, the shared project version, or a release workflow changes. **Run workflow** creates a downloadable development build without publishing a release. For a local release, run `./do build-rider` and `./do pack` before `./do release`.

## Compatibility

Rider 2025.2 and later. The plugin is pure JVM and uses only platform APIs, so it has no ReSharper backend component.
