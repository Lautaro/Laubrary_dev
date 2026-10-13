param(
    [string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\6000.4.6f1',
    [string[]]$LocalFixtures = @()
)
$ErrorActionPreference = 'Stop'
$monoRoot = Join-Path $UnityEditor 'Editor\Data\MonoBleedingEdge'
$mono = Join-Path $monoRoot 'bin\mono.exe'
$compiler = Join-Path $monoRoot 'lib\mono\4.5\csc.exe'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$proofDir = Join-Path $tempRoot ('ztracker-renoise-reader-' + [guid]::NewGuid().ToString('N'))
$proofExe = Join-Path $proofDir 'RenoiseReaderChecks.exe'
try {
    New-Item -ItemType Directory -Path $proofDir | Out-Null
    $core = Join-Path $PSScriptRoot '..\Core'
    & $mono $compiler /nologo /target:exe ('/out:' + $proofExe) /r:System.Xml.Linq.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll (Join-Path $core 'RenoiseArchive.cs') (Join-Path $core 'RenoiseCatalog.cs') (Join-Path $PSScriptRoot 'RenoiseReaderTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Reader-check compilation failed.' }
    & $mono $proofExe @LocalFixtures
    if ($LASTEXITCODE -ne 0) { throw 'Reader checks failed.' }
}
finally {
    $resolvedProof = [IO.Path]::GetFullPath($proofDir)
    if ($resolvedProof.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($resolvedProof).StartsWith('ztracker-renoise-reader-')) {
        if (Test-Path -LiteralPath $resolvedProof) { Remove-Item -LiteralPath $resolvedProof -Recurse -Force }
    }
}
