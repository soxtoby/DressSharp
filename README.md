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

`--staged` and `--changed` select only the files Git reports as changed, so a hook formats what was touched instead of the repository. A pre-commit hook is one line; `format --staged` stages what it rewrites:

```sh
dotnet dress format --staged
```

For a hook that runs after an editor or agent writes, format everything changed since the last commit:

```sh
dotnet dress format --changed
```

Both require a repository-pinned or global install of the current version. This repository's `.claude/settings.json` and `.codex/hooks.json` run the second form from Claude Code and Codex after `dotnet tool restore`.

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

## Documentation site

`./do build-docs` builds the static site into `artifacts/site` with [Astro Starlight](https://starlight.astro.build/) under Bun. The site in `website/` holds the landing page; `tools/DressSharp.Docs` writes the rest into it before each build: the CLI and compatibility documents from `docs/`, and a reference for every preference in the built-in rule catalog. Each rule's example is formatted by the real formatter under every accepted value, so the reference cannot drift from the code. After one `./do build-docs`, `bun run dev` in `website/` serves the site with live reload. The `Docs` workflow publishes the same output to GitHub Pages on every push to `master`; enable Pages with the "GitHub Actions" source once in the repository settings.
