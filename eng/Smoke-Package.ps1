param(
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ("DressSharp-smoke-" + [guid]::NewGuid().ToString("N"))
$packages = Join-Path $scratch "packages"
$globalTools = Join-Path $scratch "global-tools"
$localTools = Join-Path $scratch "local-tools"

try {
    New-Item -ItemType Directory -Path $packages, $globalTools, $localTools | Out-Null
    dotnet pack (Join-Path $root "src/DressSharp/DressSharp.csproj") -c $Configuration -o $packages
    if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed." }

    $packageFiles = @(Get-ChildItem -Path $packages -Filter "DressSharp.*.nupkg" | Where-Object Name -NotLike "*.symbols.nupkg")
    if ($packageFiles.Count -ne 1) { throw "Expected one DressSharp package; found $($packageFiles.Count)." }
    $package = $packageFiles[0]
    $version = [System.IO.Path]::GetFileNameWithoutExtension($package.Name).Substring("DressSharp.".Length)

    dotnet tool install DressSharp --tool-path $globalTools --version $version --add-source $packages --ignore-failed-sources
    if ($LASTEXITCODE -ne 0) { throw "Tool-path installation failed." }
    & (Join-Path $globalTools "dotnet-dress") --version
    if ($LASTEXITCODE -ne 0) { throw "Tool-path invocation failed." }

    Push-Location $localTools
    try {
        dotnet new tool-manifest
        dotnet tool install DressSharp --version $version --add-source $packages --ignore-failed-sources
        dotnet tool run dotnet-dress --version
        if ($LASTEXITCODE -ne 0) { throw "Local tool invocation failed." }
    }
    finally {
        Pop-Location
    }
}
finally {
    if (Test-Path -LiteralPath $scratch) {
        Remove-Item -LiteralPath $scratch -Recurse -Force
    }
}
