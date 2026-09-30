param([Parameter(Mandatory=$true)][string]$Name)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$probeFile = Join-Path $PSScriptRoot ($Name + '.cs')
$resultText = & 'C:\Users\Lauta\AppData\Local\Unity\bin\unity.exe' command eval_file $probeFile --project-path $projectRoot --json
$result = ($resultText -join "`n") | ConvertFrom-Json
$evidenceDir = Join-Path $PSScriptRoot 'Evidence'
New-Item -ItemType Directory -Force -Path $evidenceDir | Out-Null
$resultText | Set-Content -LiteralPath (Join-Path $evidenceDir ($Name + '.json')) -Encoding utf8
if (!$result.success) { $result.errors | ConvertTo-Json -Depth 8; exit 1 }
if (!$result.data.result.success) { $result.data.result | ConvertTo-Json -Depth 8; exit 1 }
$result.data.result.result
