$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output = Join-Path $repo '.benchmarks/results/shared-layout'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$source = Get-Content -LiteralPath (Join-Path $repo 'src/DressSharp/Rules/SinglePassEmitter.cs') -Raw
# PROTOTYPE: retain the current layout decisions exactly, replace only their output sink.
$source = $source.Replace('SinglePassEmitter', 'PrototypeTokenLayoutResolver')
$replacements = @{
    'readonly StringBuilder _output;' = 'readonly PlannedText _output;'
    '_output = new StringBuilder(capacity);' = '_output = new PlannedText(_pieces);'
    'internal static string Emit(' = 'internal static ResolvedTokenLayout Resolve('
    'string Run()' = 'ResolvedTokenLayout Run()'
    'return _output.ToString();' = 'return _output.Finish();'
    '            Append(_indentation.RebaseTokenText(piece.Token, _sourceIndents[index], _lineIndent));' = "            var tokenText = _indentation.RebaseTokenText(piece.Token, _sourceIndents[index], _lineIndent);`n            _output.AppendToken(index, tokenText);`n            TrackCopiedText(tokenText);"
}
foreach ($entry in $replacements.GetEnumerator()) {
    if (-not $source.Contains($entry.Key)) { throw "Emitter changed; missing prototype anchor: $($entry.Key)" }
    $source = $source.Replace($entry.Key, $entry.Value)
}
[IO.File]::WriteAllText((Join-Path $output 'PrototypeTokenLayoutResolver.cs'), "// GENERATED THROWAWAY PROTOTYPE. See prototypes/SharedLayout/README.md.`n" + $source)
