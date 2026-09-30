$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$evidence = Join-Path $PSScriptRoot 'Evidence\Foundations'
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
function Probe([string]$Name) {
    & (Join-Path $PSScriptRoot 'run_probe.ps1') -Name $Name
    if ($LASTEXITCODE -ne 0) { throw "Probe failed: $Name" }
}
Probe 'OpenFoundations'
$cases = @(@{label='foundation_720_default'; change=$null}, @{label='foundation_720_override'; change='FoundationOverride'}, @{label='foundation_720_restored'; change='FoundationOverride'}, @{label='foundation_420_default'; change='FoundationNarrow'}, @{label='foundation_900_default'; change='FoundationWide'})
foreach ($case in $cases) {
    if ($case.change) { Probe $case.change }
    Probe 'FoundationStateProbe'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Evidence\FoundationStateProbe.json') -Destination (Join-Path $evidence ($case.label + '_state.json'))
    Probe 'CaptureFoundations'
    $sourceLabel = $case.label.Replace('_restored', '_default')
    foreach ($side in @('baseline', 'candidate')) { Copy-Item -LiteralPath (Join-Path $env:LOCALAPPDATA ('Temp\ZoundsUitkCompare\' + $sourceLabel + '_' + $side + '.png')) -Destination (Join-Path $evidence ($case.label + '_' + $side + '.png')) }
    $comparison = & python (Join-Path $PSScriptRoot 'compare_captures.py') (Join-Path $evidence ($case.label + '_baseline.png')) (Join-Path $evidence ($case.label + '_candidate.png')) --crop-top 60 --tolerance 0
    if ($LASTEXITCODE -ne 0) { throw "Comparison failed: $($case.label)" }
    $comparison | Set-Content -LiteralPath (Join-Path $evidence ($case.label + '_comparison.json')) -Encoding utf8
    $parsed = ($comparison -join "`n") | ConvertFrom-Json
    if ($case.label.EndsWith('_override')) { if ($parsed.exact_changed_pixels -eq 0) { throw 'Override had no visible effect' } }
    elseif ($parsed.exact_changed_pixels -ne 0) { throw "Unexpected visual drift: $($case.label) $($parsed.exact_changed_pixels) pixels" }
    Write-Output "$($case.label): $($parsed.exact_changed_pixels) differing pixels"
}
