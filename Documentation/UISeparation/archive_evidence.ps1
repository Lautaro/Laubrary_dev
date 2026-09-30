$ErrorActionPreference='Stop'
$sourceDir=Join-Path $env:TEMP 'ZoundsUitkCompare'
$evidenceDir=Join-Path $PSScriptRoot 'Evidence'
New-Item -ItemType Directory -Force -Path $evidenceDir | Out-Null
$labels=@('default-normal','default-repeat','default-narrow','default-wide','colorful-normal','colorful-narrow','colorful-wide','numeric-input','empty-state','scoped-override')
$summary=@()
foreach($label in $labels) {
    foreach($suffix in @('baseline','candidate','pair','diff')) {
        $name=$label+'_'+$suffix+'.png'
        Copy-Item -LiteralPath (Join-Path $sourceDir $name) -Destination (Join-Path $evidenceDir $name) -Force
    }
    $baseline=Join-Path $evidenceDir ($label+'_baseline.png')
    $candidate=Join-Path $evidenceDir ($label+'_candidate.png')
    $json=& python (Join-Path $PSScriptRoot 'compare_captures.py') $baseline $candidate --crop-top 60
    if($LASTEXITCODE -ne 0) { throw "Comparison failed: $label" }
    $json | Set-Content -LiteralPath (Join-Path $evidenceDir ($label+'.comparison.json')) -Encoding utf8
    $r=($json -join "`n") | ConvertFrom-Json
    $summary+=[pscustomobject]@{case=$label;changedPixels=$r.exact_changed_pixels;maxDelta=$r.max_channel_delta;width=$r.size.width;height=$r.size.height;intentionalDifference=($label -eq 'scoped-override')}
}
$repeat=& python (Join-Path $PSScriptRoot 'compare_captures.py') (Join-Path $evidenceDir 'default-normal_baseline.png') (Join-Path $evidenceDir 'default-repeat_baseline.png') --crop-top 60
$repeat | Set-Content -LiteralPath (Join-Path $evidenceDir 'unchanged-baseline.comparison.json') -Encoding utf8
$summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidenceDir 'comparison-summary.json') -Encoding utf8
$summary | Format-Table -AutoSize
if(@($summary | Where-Object { !$_.intentionalDifference -and $_.changedPixels -ne 0 }).Count -gt 0) { throw 'Unexpected visual difference' }
if((($repeat -join "`n") | ConvertFrom-Json).exact_changed_pixels -ne 0) { throw 'Reference capture is not repeatable' }
