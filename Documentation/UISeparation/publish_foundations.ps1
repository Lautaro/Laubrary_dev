$ErrorActionPreference = 'Stop'
$project = 'Laubrary_Dev'
$base = 'http://127.0.0.1:8778'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$report = Join-Path $PSScriptRoot 'Shared Foundations Report.md'
$drive = 'D:\Claude@GDrive\Laubrary UI Separation - Shared Foundations Report.md'
Copy-Item -LiteralPath $report -Destination $drive
Copy-Item -LiteralPath $report -Destination 'D:\Claude@GDrive\Laubrary UI Separation - Implementation Progress.md'
$items = @(
    @{id='T-0530'; text='Audited the shared foundation boundaries and established independent references. Scope is the first phase-2 foundation rollout in the isolated copy.'; docs=@('Shared Foundations Report.md','FOUNDATION_ACCEPTANCE.md')},
    @{id='T-0531'; text='Moved card/header and generated-field static presentation into semantic USS parts while preserving state, binding and caller contracts. Frozen/candidate views match at three widths.'; docs=@('FoundationContainers.md','FoundationFields.md')},
    @{id='T-0532'; text='Moved ordinary factory defaults and structural column layout into USS, with reset-safe responsive width/gutter inputs. Explicit caller values remain compatible. Default and restored layouts match frozen references exactly.'; docs=@('FoundationFactories.md','FoundationFlow.md')},
    @{id='T-0533'; text='Added immutable per-root presentation snapshots and complete bare-host popup attachment. Verified independent tool scopes, optional legacy skin classes, class filtering, sheet deduplication and Escape dismissal.'; docs=@('PresentationContext.md')},
    @{id='T-0534'; text='Integrated and verified the first shared-foundation rollout. Exact default parity at three widths, independent parent overrides/reset, real BackSplash browser/editor parity and workflow, and 14 earlier pilot regressions pass. Whole-tool and runtime rollout remain future scope.'; docs=@('Shared Foundations Report.md','FOUNDATION_ACCEPTANCE.md','Evidence\Foundations\foundation_720_override_candidate.png','Evidence\Foundations\foundation_consumer_edited.png')},
    @{id='T-0535'; text='Extracted the shared Toolkit asset-browser shell presentation. Preserved filtering, selection and asset editing. Frozen/current BackSplash library and editing views match exactly; browser-to-edit, save, Undo and cold reopen pass on an isolated fixture.'; docs=@('FoundationAssetBrowser.md')},
    @{id='T-0536'; text='Added an occurrence-based presentation regression guard with specific reviewed compatibility/dynamic exceptions. Current sources pass; an injected new cosmetic assignment fails its self-test. The guard is deliberately not an exhaustive proof over every painter API.'; docs=@('PresentationGuard.md')},
    @{id='T-0522'; text='Continued the approved plan in the isolated project: first shared-foundation rollout implemented and verified, committed as 0a347d4. Attached continuation report is identical to the Drive copy. This does not mark all tools or runtime migrated.'; docs=@('Shared Foundations Report.md')}
)
foreach ($item in $items) {
    $workspace = Join-Path 'D:\UNITY\Laubrary Dev\.agenthq\workspace' $item.id
    New-Item -ItemType Directory -Force -Path $workspace | Out-Null
    $attachments = @()
    foreach($doc in $item.docs) {
        $source = Join-Path $PSScriptRoot $doc
        $destination = Join-Path $workspace (Split-Path $source -Leaf)
        Copy-Item -LiteralPath $source -Destination $destination
        $attachments += $destination
    }
    $payload = @{project=$project; id=$item.id; text=$item.text; details='Implemented only in D:/UNITY/Laubrary Dev - UI Separation. Commit 0a347d4. Coordinator reviewed delegated work and ran serial Unity verification. See report for verified-by-probe, verified-by-eye and unverified coverage; explicit compatibility paths remain.'; technicalDetails='Reference commit e52505a. Scripts and artifacts under Documentation/UISeparation; final evidence under Evidence/Foundations. No original source or consumer deployment.'; proof=@(@{text='Recorded scoped implementation and verification evidence in isolated copy';checked=$true}); attachments=$attachments; newStatus='done'}
    $reply = Invoke-RestMethod ($base + '/api/task/handover') -Method Post -ContentType 'application/json' -Body ($payload | ConvertTo-Json -Depth 12)
    if ($reply.ok -eq $false) { throw "Handover failed: $($item.id)" }
    $reply | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath (Join-Path $PSScriptRoot ('Evidence\Foundations\AHQ-' + $item.id + '.json')) -Encoding utf8
    $task = Invoke-RestMethod ($base + '/api/task?project=' + $project + '&id=' + $item.id)
    $tags = (([string]$task.frontmatter.tags -split ',') | ForEach-Object {$_.Trim()} | Where-Object {$_ -and $_ -ne 'uncommitted'}) -join ', '
    Invoke-RestMethod ($base + '/api/task/edit') -Method Post -ContentType 'application/json' -Body (@{project=$project;id=$item.id;tags=$tags}|ConvertTo-Json) | Out-Null
    Write-Output ($item.id + ': ' + $task.frontmatter.status)
}
$sourceHash = (Get-FileHash -LiteralPath $report -Algorithm SHA256).Hash
if ((Get-FileHash -LiteralPath $drive -Algorithm SHA256).Hash -ne $sourceHash) { throw 'Drive report mismatch' }
Write-Output ('Identical Drive report SHA256: ' + $sourceHash)
