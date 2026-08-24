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

See [CLI and configuration](docs/cli-and-configuration.md), the [rule reference](docs/rules-v1.md), and the [compatibility policy](docs/compatibility.md).
