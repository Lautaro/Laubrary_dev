$ErrorActionPreference='Stop'
$common='Verified in the isolated copy only. Unity 6000.3.10f1 compiled; frozen-reference comparisons are exact at 420/620/900 widths in both presentation modes; live interaction/Undo, scoped overrides, reset, reopening and domain reload passed. Full evidence and limitations are in ACCEPTANCE.md. Baseline 9eb3d9c; implementation 0c3841a.'
$items=@(
    @{id='T-0523';text='Phase 0: independent project copy and immutable baseline completed.';files=@('CONTRACT.md','Inventory/BASELINE_IDENTITY.md')},
    @{id='T-0524';text='Phase 0: source-based surface register and all-family migration classification completed; unknowns explicitly retained.';files=@('Inventory/MIGRATION_CLASSIFICATION.md','Inventory/migration-classification.csv','Inventory/BASELINE_IDENTITY.md')},
    @{id='T-0525';text='Phase 1: shared standard-control styles and semantic tags integrated; parent-scoped styling and reset verified.';files=@('StandardControls.md','Authoring.md')},
    @{id='T-0526';text='Phase 1: band/range presentation separated, including optional USS thumb/track metrics; default parity and changed-metric interactions verified.';files=@('BandsRange.md')},
    @{id='T-0527';text='Phase 1: envelope presentation adapter and USS profile integrated; painted geometry and hit testing agree under scoped padding, with fallback restoration verified.';files=@('Envelope.md')},
    @{id='T-0528';text='Phase 1: independent-reference comparison pilot, UXML shell, responsive row and visible designer controls completed.';files=@('PilotHarness.md','Evidence/default-normal_pair.png','Evidence/scoped-override_pair.png')},
    @{id='T-0529';text='Phases 0 and 1 accepted. Compilation, exact visual comparisons and live interaction/Undo checks completed; full report attached and copied to Drive.';files=@('Phase 0 and 1 Report.md','ACCEPTANCE.md','Evidence/comparison-summary.json','Evidence/default-normal_pair.png','Evidence/colorful-normal_pair.png','Evidence/scoped-override_pair.png')},
    @{id='T-0522';text='Follow-up to the planning draft: authorised phases 0 and 1 completed in a separate physical project copy. Implementation is organised under UI separation - phases 0 and 1 (T-0523–T-0529). The milestone report is attached and the identical Markdown is in the Drive folder.';files=@('Phase 0 and 1 Report.md')}
)
foreach($item in $items) {
    # AHQ accepts attachments from its registered project folder only. Stage documents in its task workspace.
    $publishDir=Join-Path ('D:\UNITY\Laubrary Dev\.agenthq\workspace\'+$item.id) 'ui-separation-phase01'
    New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
    $attachments=@($item.files | ForEach-Object {
        $source=(Resolve-Path (Join-Path $PSScriptRoot $_)).Path
        $destination=Join-Path $publishDir ([IO.Path]::GetFileName($source))
        Copy-Item -LiteralPath $source -Destination $destination -Force
        $destination
    })
    $body=@{project='Laubrary_Dev';id=$item.id;text=$item.text;details=$common;technicalDetails='Project: D:\UNITY\Laubrary Dev - UI Separation. Menu: Laubrary/UI Separation Pilot. Original project was not refactored. No runtime/consumer-wide acceptance claim. Four GPT-5.6 Terra workers provided bounded implementation/inventory work; GPT-6 Astra coordinated, reviewed, corrected and verified.';proof=@(@{text='Evidence reviewed by coordinator; see acceptance record and per-task attached artifacts.';checked=$true});attachments=$attachments;newStatus='done'} | ConvertTo-Json -Depth 8
    $response=Invoke-RestMethod -Uri 'http://127.0.0.1:8778/api/task/handover' -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
    $response | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $PSScriptRoot ('Evidence/AHQ-'+$item.id+'.json')) -Encoding utf8
    Write-Output ($item.id+': '+$response.frontmatter.status+'; attachments='+@($response.attached).Count)
}
