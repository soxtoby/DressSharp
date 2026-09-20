# DressSharp

DressSharp is a syntax-only, explicitly configured C# formatter distributed as one .NET tool package.

## Install

```powershell
dotnet tool install --global DressSharp
dotnet dress --version
```

For a repository-pinned install:

```powershell
dotnet new tool-manifest
dotnet tool install DressSharp
```

## Hooks

`--staged` and `--changed` select only the files Git reports as changed, so a hook formats what was touched instead of the repository. For a pre-commit hook, format the staged files and stage the result again:

```sh
dotnet dress format --staged && git diff --name-only --cached -- '*.cs' | xargs -r git add
```

For a hook that runs after an editor or agent writes, format everything changed since the last commit:

```sh
dotnet dress format --changed
```

Both require a repository-pinned or global install of the current version.

## Development

Build, test, and format DressSharp from the current checkout:

```powershell
./do test
./do format-self
```

To test the packed local tool:

```powershell
dotnet tool restore
./do install-local
dotnet tool run dotnet-dress -- --version
```

The package installs one command: `dotnet dress`.

See [CLI and configuration](docs/cli-and-configuration.md) and the [compatibility policy](docs/compatibility.md).
