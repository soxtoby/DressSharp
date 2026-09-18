param([switch]$Benchmark, [switch]$Braces, [string]$AssetsFile)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$manifest = Get-Content -LiteralPath (Join-Path $repo '.benchmarks/corpus/manifest.json') -Raw | ConvertFrom-Json
foreach ($entry in $manifest.files) {
    $path = Join-Path $repo ('.benchmarks/corpus/files/' + $entry.path)
    if ((Get-FileHash -LiteralPath $path).Hash -ne $entry.sha256) { throw "Corpus mismatch: $($entry.path)" }
}
$buildArgs = @('build', (Join-Path $PSScriptRoot 'SharedLayout.csproj'), '-c', 'Release', '--nologo')
if ($AssetsFile) { $buildArgs += @('--no-restore', ('-p:ProjectAssetsFile=' + [IO.Path]::GetFullPath($AssetsFile))) }
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw 'Prototype build failed' }
$previousTiering = $env:DOTNET_TieredCompilation
try {
    $env:DOTNET_TieredCompilation = '0'
    $runArgs = @((Join-Path $PSScriptRoot 'bin/Release/net10.0/DressSharp.UnitTests.dll'))
    if ($Benchmark) { $runArgs += '--benchmark' }
    if ($Braces) { $runArgs += '--braces' }
    & dotnet @runArgs
    if ($LASTEXITCODE -ne 0) { throw 'Prototype parity failed; inspect .benchmarks/results/shared-layout/verification.json' }
} finally { $env:DOTNET_TieredCompilation = $previousTiering }
