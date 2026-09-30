$ErrorActionPreference = 'Stop'
$evidence = Join-Path $PSScriptRoot 'Evidence\Foundations'
$tempCaptures = Join-Path $env:LOCALAPPDATA 'Temp\ZoundsUitkCompare'
foreach ($label in @('default-normal', 'colorful-normal')) {
    foreach ($side in @('baseline','candidate')) { Copy-Item -LiteralPath (Join-Path $tempCaptures ($label + '_' + $side + '.png')) -Destination (Join-Path $evidence ($label + '_' + $side + '.png')) }
}
foreach ($label in @('default-normal','colorful-normal','foundation_consumer','foundation_browser')) {
    $result = & python (Join-Path $PSScriptRoot 'compare_captures.py') (Join-Path $evidence ($label + '_baseline.png')) (Join-Path $evidence ($label + '_candidate.png')) --crop-top 60 --tolerance 0
    if ($LASTEXITCODE -ne 0) { throw "Comparison failed: $label" }
    $result | Set-Content -LiteralPath (Join-Path $evidence ($label + '_comparison.json')) -Encoding utf8
    $comparison = ($result -join "`n") | ConvertFrom-Json
    if ($comparison.exact_changed_pixels -ne 0) { throw "Visual drift: $label $($comparison.exact_changed_pixels)" }
    Write-Output "$label exact match"
}
foreach($name in @('FoundationCompileProbe','FoundationInteractionProbe','FoundationContextSetup','FoundationContextProbe','FoundationContextFinish','FoundationBrowserOpen','FoundationBrowserSelect','FoundationConsumerEdit','FoundationConsumerUndo','FoundationConsumerReopen','InteractionSuite')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('Evidence\' + $name + '.json')) -Destination (Join-Path $evidence ($name + '.json'))
}
& python (Join-Path $PSScriptRoot 'check_presentation.py') | Tee-Object -FilePath (Join-Path $evidence 'presentation-guard.txt')
if ($LASTEXITCODE -ne 0) { throw 'Presentation guard failed' }
& python (Join-Path $PSScriptRoot 'check_presentation.py') --self-test | Tee-Object -FilePath (Join-Path $evidence 'presentation-guard-selftest.txt')
if ($LASTEXITCODE -ne 0) { throw 'Presentation guard self-test failed' }
