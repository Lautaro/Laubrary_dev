<#
    sync-laubrary-to-consumers.ps1

    Wholesale-copies the dev-host Laubrary package into each consumer project's embedded package
    folder, backing up the current copy first. This is a DELIBERATE, manual checkpoint sync - NOT
    auto-latest. Run it when you are ready to also handle any breaking-change fallout in the
    consumers (renames, namespace changes, SerializeReference migrations).

    Usage (from a PowerShell prompt):
        .\sync-laubrary-to-consumers.ps1              # sync every target, with backups
        .\sync-laubrary-to-consumers.ps1 -DryRun      # preview only, change nothing
        .\sync-laubrary-to-consumers.ps1 -Skip OutBurner
        .\sync-laubrary-to-consumers.ps1 -Only TinyWar,TrueEye

    Tip: closing a consumer's Unity editor before syncing it avoids a big mid-session reimport,
    but it is not required - Unity re-ingests the package on next focus / AssetDatabase refresh.
#>
param(
    [string[]]$Skip = @(),
    [string[]]$Only = @(),
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

# ---- Config (edit to taste) --------------------------------------------------------------------
$Src        = 'D:\UNITY\Laubrary Dev\Assets\Packages\Laubrary'
$BackupRoot = 'D:\UNITY\_LaubraryUpgradeBackup'

# Name -> the consumer's embedded package folder. NOTE: Asteroid+ uses a folder literally named
# "Laubrary" (Unity auto-discovers embedded packages by folder PRESENCE, not by name); the rest
# use "com.lautaro.arino.laubrary".
$Consumers = @(
    @{ Name = 'TinyWar';     Path = 'D:\UNITY\TinyWar\Packages\com.lautaro.arino.laubrary' }
    @{ Name = 'RiskyRemake'; Path = 'D:\UNITY\RiskyRemake\Packages\com.lautaro.arino.laubrary' }
    @{ Name = 'TrueEye';     Path = 'D:\UNITY\TrueEye\Packages\com.lautaro.arino.laubrary' }
    @{ Name = 'OutBurner';   Path = 'D:\UNITY\OutBurner\Packages\com.lautaro.arino.laubrary' }
    @{ Name = 'Asteroid+';   Path = 'D:\UNITY\Asteroid+\Packages\Laubrary' }
)
# ------------------------------------------------------------------------------------------------

if (-not (Test-Path -LiteralPath $Src)) { Write-Error "Source package not found: $Src"; exit 1 }

$targets = $Consumers
if ($Only.Count) { $targets = $targets | Where-Object { $Only -contains $_.Name } }
if ($Skip.Count) { $targets = $targets | Where-Object { $Skip -notcontains $_.Name } }
if (-not $targets) { Write-Host "No targets selected." -ForegroundColor Yellow; exit 0 }

$stamp = Get-Date -Format 'yyyy-MM-dd_HHmmss'
$bkDir = Join-Path $BackupRoot $stamp

Write-Host "Laubrary sync" -ForegroundColor Cyan
Write-Host "  source : $Src"
Write-Host "  targets: $($targets.Name -join ', ')"
if ($DryRun) { Write-Host "  DRY RUN - nothing will be changed" -ForegroundColor Yellow }
Write-Host ""

$done = @()
foreach ($c in $targets) {
    $name = $c.Name
    $dst  = $c.Path
    Write-Host "=== $name ===" -ForegroundColor Green

    $pkgParent = Split-Path -LiteralPath $dst -Parent          # ...\<Project>\Packages
    $projRoot  = Split-Path -LiteralPath $pkgParent -Parent     # ...\<Project>

    # Guard: the consumer must already be on the migrated in-package ZUI layout. A leftover
    # Assets\ZUI collides on 1200+ icon GUIDs and duplicates the ZUI.Editor asmdef name.
    if (Test-Path -LiteralPath (Join-Path $projRoot 'Assets\ZUI')) {
        Write-Host "  SKIP - $name still has Assets\ZUI (do the one-time structural migration first)" -ForegroundColor Red
        continue
    }
    if (-not (Test-Path -LiteralPath $pkgParent)) {
        Write-Host "  SKIP - no Packages folder at $pkgParent" -ForegroundColor Red
        continue
    }

    if ($DryRun) {
        if (Test-Path -LiteralPath $dst) { Write-Host "  would back up, then REPLACE $dst" }
        else                             { Write-Host "  would CREATE $dst (no existing copy)" }
        continue
    }

    # Back up the current copy (if any), then wholesale-replace.
    if (Test-Path -LiteralPath $dst) {
        $bk = Join-Path $bkDir "$name\PACKAGE"
        New-Item -ItemType Directory -Force -Path (Split-Path -LiteralPath $bk -Parent) | Out-Null
        Copy-Item -LiteralPath $dst -Destination $bk -Recurse -Force
        Write-Host "  backed up -> $bk"
        Remove-Item -LiteralPath $dst -Recurse -Force
    }
    Copy-Item -LiteralPath $Src -Destination $dst -Recurse -Force

    $iconsPath = Join-Path $dst 'Zui\SystemAssets\Icons~'
    $icons = @(Get-ChildItem -LiteralPath $iconsPath -Filter '*.png' -ErrorAction SilentlyContinue).Count
    Write-Host "  synced ($icons icons embedded in Icons~)" -ForegroundColor Green
    $done += $name
}

Write-Host ""
if ($DryRun) { Write-Host "Dry run complete - no changes made." -ForegroundColor Yellow; exit 0 }

Write-Host "Synced: $($done -join ', ')" -ForegroundColor Cyan
Write-Host "Backups this run: $bkDir"
Write-Host ""
Write-Host "Per synced project, then:" -ForegroundColor Cyan
Write-Host "  1. Open it in Unity, let it reimport, confirm the Console compiles clean."
Write-Host "  2. If Laubrary made a breaking rename / namespace change this cycle, update the"
Write-Host "     consumer's own call sites to match (check the package CHANGELOG)."
Write-Host "  3. Do NOT save a .asset until you've confirmed its [SerializeReference] stacks are"
Write-Host "     intact - managed refs can null against a changed assembly. Judge asset diffs by"
Write-Host "     REMOVED lines only."
