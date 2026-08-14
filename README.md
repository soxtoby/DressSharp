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
dotnet dress --version
```

```powershell
dotnet build DressSharp.slnx
dotnet test DressSharp.slnx --no-build
dotnet pack src/DressSharp/DressSharp.csproj
pwsh eng/Smoke-Package.ps1
```

The package installs one command: `dotnet dress`.

See [CLI and configuration](docs/cli-and-configuration.md), the [rule reference](docs/rules-v1.md), and the [compatibility policy](docs/compatibility.md).
