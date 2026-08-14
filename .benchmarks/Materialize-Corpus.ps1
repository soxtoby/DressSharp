param([switch] $UpdateLock)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$definition = Get-Content -Raw (Join-Path $PSScriptRoot "corpus/sources.json") | ConvertFrom-Json
$corpus = Join-Path $PSScriptRoot "corpus/files"
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ("DressSharp-corpus-" + [guid]::NewGuid().ToString("N"))

try {
    New-Item -ItemType Directory -Path $scratch | Out-Null
    if (Test-Path -LiteralPath $corpus) { Remove-Item -LiteralPath $corpus -Recurse -Force }
    New-Item -ItemType Directory -Path $corpus | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "corpus/Corpus.csproj") -Destination (Join-Path $corpus "Corpus.csproj")
    Copy-Item -LiteralPath (Join-Path $root "tests/DressSharp.UnitTests/Fixtures/Familiar.editorconfig") -Destination (Join-Path $corpus ".editorconfig")

    foreach ($source in $definition.sources) {
        $checkout = Join-Path $scratch $source.name
        git clone --quiet --filter=blob:none --no-checkout $source.url $checkout
        if ($LASTEXITCODE -ne 0) { throw "Clone failed for $($source.name)." }
        git -C $checkout -c core.longpaths=true checkout --quiet $source.revision
        if ($LASTEXITCODE -ne 0) { throw "Checkout failed for $($source.name)." }

        $files = @(Get-ChildItem -LiteralPath $checkout -Recurse -Filter *.cs | Where-Object {
            $_.FullName -notmatch '[\\/](\.git|bin|obj|Generated)[\\/]' -and $_.Name -notmatch '\.(g|generated)\.cs$'
        } | Sort-Object { [System.IO.Path]::GetRelativePath($checkout, $_.FullName) })
        if ($files.Count -lt $source.count) { throw "$($source.name) has only $($files.Count) eligible files." }

        foreach ($file in $files | Select-Object -First $source.count) {
            $relative = [System.IO.Path]::GetRelativePath($checkout, $file.FullName)
            $destination = Join-Path (Join-Path $corpus $source.name) $relative
            New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $destination
        }
    }

    $arguments = @("run", "--project", (Join-Path $root "benchmarks/CorpusInspector/CorpusInspector.csproj"), "--", $corpus, (Join-Path $PSScriptRoot "corpus/manifest.json"))
    if ($UpdateLock) { $arguments += "--update" }
    dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Corpus verification failed." }
}
finally {
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
