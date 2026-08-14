param(
    [ValidateSet("check", "format")] [string] $Mode = "check",
    [string] $PowerMode = "unknown",
    [string] $BackgroundLoadCaveats = "none observed",
    [int] $Warmups = 3,
    [int] $Measurements = 15,
    [int[]] $WorkerProfiles,
    [ValidateRange(1, 1000)] [int] $FileCount = 1000
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$corpus = Join-Path $PSScriptRoot "corpus/files"
$manifest = Get-Content -Raw (Join-Path $PSScriptRoot "corpus/manifest.json") | ConvertFrom-Json
$results = Join-Path $PSScriptRoot "results"
$timestamp = [DateTimeOffset]::UtcNow
New-Item -ItemType Directory -Path $results -Force | Out-Null

dotnet build (Join-Path $root "src/DressSharp/DressSharp.csproj") -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Release build failed." }
pwsh -NoProfile -File (Join-Path $PSScriptRoot "Materialize-Corpus.ps1")
if ($LASTEXITCODE -ne 0) { throw "Corpus verification failed." }
dotnet restore (Join-Path $corpus "Corpus.csproj") --nologo
if ($LASTEXITCODE -ne 0) { throw "Corpus restore failed." }
$benchmarkCorpus = $corpus
$subset = $null
if ($FileCount -lt 1000) {
    $subset = Join-Path ([System.IO.Path]::GetTempPath()) ("DressSharp-subset-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path (Join-Path $subset "files") -Force | Out-Null
    Copy-Item (Join-Path $corpus "Corpus.csproj"), (Join-Path $corpus ".editorconfig") -Destination $subset
    $fileIndex = 0
    foreach ($file in Get-ChildItem $corpus -Recurse -Filter *.cs | Sort-Object FullName | Select-Object -First $FileCount) {
        $destination = Join-Path (Join-Path $subset "files") ("{0:D4}-{1}" -f $fileIndex, $file.Name)
        Copy-Item -LiteralPath $file.FullName -Destination $destination
        $fileIndex++
    }
    dotnet restore (Join-Path $subset "Corpus.csproj") --nologo
    if ($LASTEXITCODE -ne 0) { throw "Subset restore failed." }
    $benchmarkCorpus = $subset
}

function Invoke-Measured([string] $Tool, [int] $Workers, [bool] $Measured) {
    $runRoot = $benchmarkCorpus
    $scratch = $null
    if ($Mode -eq "format") {
        $scratch = Join-Path ([System.IO.Path]::GetTempPath()) ("DressSharp-run-" + [guid]::NewGuid().ToString("N"))
        Copy-Item -LiteralPath $benchmarkCorpus -Destination $scratch -Recurse
        $runRoot = $scratch
    }
    $timingPath = Join-Path ([System.IO.Path]::GetTempPath()) ("DressSharp-timing-" + [guid]::NewGuid().ToString("N") + ".json")
    try {
        $info = [System.Diagnostics.ProcessStartInfo]::new("dotnet")
        $info.UseShellExecute = $false
        $info.RedirectStandardOutput = $true
        $info.RedirectStandardError = $true
        $info.WorkingDirectory = $root
        if ($Tool -eq "DressSharp") {
            foreach ($argument in @("run", "--no-build", "-c", "Release", "--project", "src/DressSharp/DressSharp.csproj", "--", $Mode, $runRoot)) { $info.ArgumentList.Add($argument) }
            $info.Environment["DRESSSHARP_BENCHMARK_WORKERS"] = $Workers
            $info.Environment["DRESSSHARP_BENCHMARK_TIMING"] = $timingPath
        }
        else {
            foreach ($argument in @("format", (Join-Path $runRoot "Corpus.csproj"), "--no-restore", "--verbosity", "quiet")) { $info.ArgumentList.Add($argument) }
            if ($Mode -eq "check") { $info.ArgumentList.Add("--verify-no-changes") }
        }
        $process = [System.Diagnostics.Process]::Start($info)
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $process.Refresh()
        $acceptedExitCodes = if ($Tool -eq "dotnet format" -and $Mode -eq "check") { @(0, 2) } else { @(0, 1) }
        if ($process.ExitCode -notin $acceptedExitCodes) { throw "$Tool failed: $($stderr.Result)" }
        if (-not $Measured) { return $null }
        $timing = if (Test-Path -LiteralPath $timingPath) { Get-Content -Raw $timingPath | ConvertFrom-Json } else { $null }
        $elapsed = ($process.ExitTime - $process.StartTime).TotalMilliseconds
        if ($timing) { $timing | Add-Member -NotePropertyName startupAndDiscoveryMilliseconds -NotePropertyValue ([math]::Max(0, $elapsed - $timing.wallMilliseconds)) }
        $peakWorkingSet = try { $process.PeakWorkingSet64 } catch { 0 }
        return [ordered]@{ tool = $Tool; wallMilliseconds = $elapsed; cpuMilliseconds = $process.TotalProcessorTime.TotalMilliseconds; peakWorkingSetBytes = $peakWorkingSet; exitCode = $process.ExitCode; dressSharpTiming = $timing }
    }
    finally {
        if (Test-Path -LiteralPath $timingPath) { Remove-Item -LiteralPath $timingPath -Force }
        if ($scratch -and (Test-Path -LiteralPath $scratch)) { Remove-Item -LiteralPath $scratch -Recurse -Force }
    }
}

function Get-Summary($runs, [string] $tool) {
    $values = @($runs | Where-Object tool -eq $tool | ForEach-Object wallMilliseconds | Sort-Object)
    $median = $values[[math]::Floor($values.Count / 2)]
    $p95 = $values[[math]::Ceiling($values.Count * .95) - 1]
    [ordered]@{ medianMilliseconds = $median; p95Milliseconds = $p95; minimumMilliseconds = $values[0]; maximumMilliseconds = $values[-1] }
}

$profiles = @()
$defaultWorkers = [math]::Min([math]::Max([math]::Floor([Environment]::ProcessorCount / 2), 1), 16)
if (-not $WorkerProfiles) { $WorkerProfiles = @($defaultWorkers, 1, [Environment]::ProcessorCount) | Select-Object -Unique }
foreach ($workers in $WorkerProfiles) {
    for ($warmup = 0; $warmup -lt $Warmups; $warmup++) {
        Invoke-Measured "DressSharp" $workers $false | Out-Null
        Invoke-Measured "dotnet format" $workers $false | Out-Null
    }
    $runs = @()
    for ($iteration = 0; $iteration -lt $Measurements; $iteration++) {
        $order = if ($iteration % 2 -eq 0) { @("DressSharp", "dotnet format") } else { @("dotnet format", "DressSharp") }
        foreach ($tool in $order) { $runs += Invoke-Measured $tool $workers $true }
    }
    $dress = Get-Summary $runs "DressSharp"
    $format = Get-Summary $runs "dotnet format"
    $profiles += [ordered]@{ workers = $workers; runs = $runs; summary = [ordered]@{ dressSharp = $dress; dotnetFormat = $format; medianRatio = $dress.medianMilliseconds / $format.medianMilliseconds } }
}

$dotnetInfo = dotnet --info | Out-String
$result = [ordered]@{
    schemaVersion = 1; timestamp = $timestamp.ToString("O"); mode = $Mode
    environment = [ordered]@{ cpu = $env:PROCESSOR_IDENTIFIER ?? [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString(); logicalCores = [Environment]::ProcessorCount; ramBytes = [System.GC]::GetGCMemoryInfo().TotalAvailableMemoryBytes; os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription; powerMode = $PowerMode; dotnetSdk = (dotnet --version); gitCommit = (git rev-parse HEAD); backgroundLoadCaveats = $BackgroundLoadCaveats }
    corpus = [ordered]@{ version = $manifest.version; hash = $manifest.corpusHash; fileCount = $FileCount }
    tools = [ordered]@{ dressSharp = "0.1.0"; dotnetFormat = ($dotnetInfo -split "`n" | Where-Object { $_ -match 'Version:' } | Select-Object -First 1).Trim(); roslyn = "5.6.0" }
    profiles = $profiles
}
$stem = $timestamp.ToString("yyyyMMddTHHmmssZ") + "-$Mode"
$jsonPath = Join-Path $results "$stem.json"
$result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonPath
$lines = @("# Benchmark $($timestamp.ToString('u'))", "", "Mode: $Mode  ", "Corpus: $($manifest.corpusHash)", "", "| Workers | DressSharp median | DressSharp p95 | dotnet format median | Ratio |", "| ---: | ---: | ---: | ---: | ---: |")
foreach ($profile in $profiles) { $lines += "| $($profile.workers) | $([math]::Round($profile.summary.dressSharp.medianMilliseconds, 2)) ms | $([math]::Round($profile.summary.dressSharp.p95Milliseconds, 2)) ms | $([math]::Round($profile.summary.dotnetFormat.medianMilliseconds, 2)) ms | $([math]::Round($profile.summary.medianRatio, 3)) |" }
$lines | Set-Content -LiteralPath (Join-Path $results "$stem.md")
Write-Output $jsonPath
if ($subset -and (Test-Path -LiteralPath $subset)) { Remove-Item -LiteralPath $subset -Recurse -Force }
