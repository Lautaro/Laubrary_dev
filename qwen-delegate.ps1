<#
.SYNOPSIS
  Delegate a bounded coding task to the local Qwen3-Coder worker.

.DESCRIPTION
  Hermes helper for Codex-CLI. Wraps `qwen -p` with safe defaults so Codex never has to
  remember the run-limits / approval-mode / model flags. Invoked from the project root
  by AGENTS.md's delegation policy.

  Defaults: --model qwen3-coder:30b --approval-mode auto --max-wall-time 10m
            --max-tool-calls 50 --max-session-turns 40 --output-format json

  Override individual flags via parameters when a task genuinely needs different limits
  (e.g. bigger refactor -> -MaxToolCalls 100). Always keep some run limit set.

.PARAMETER Task
  The bounded, self-contained task for Qwen. Should include objective, constraints,
  paths/scope, allowed modifications, forbidden modifications, verification requirements,
  and expected report shape.

.PARAMETER Model
  Override the model id (default: qwen3-coder:30b).

.PARAMETER WallTime
  Override the wall-time limit. Default 10m.

.PARAMETER MaxToolCalls
  Override max tool calls. Default 50.

.PARAMETER MaxSessionTurns
  Override max session turns. Default 40.

.PARAMETER ApprovalMode
  Override approval mode (auto|plan|default). Default auto.
  Use 'plan' for read-only inspect tasks (forces Qwen to only propose, not execute).

.EXAMPLE
  .\qwen-delegate.ps1 -Task "Find all references to IInventoryService in this repo. Return a markdown table of file:line for each hit. Do not modify any files."

.EXAMPLE
  .\qwen-delegate.ps1 -Task "Implement the CooldownTimer class per spec at .\specs\CooldownTimer.md. Write tests under Assets\Scripts\Tests\. Compile with dotnet build ./Assets/Tests/Tests.csproj and fix any straightforward compile errors. Return: changed files, build status, remaining issues."

.EXAMPLE
  # Read-only inspect
  .\qwen-delegate.ps1 -Task "Inspect this repo and describe its three most central C# files. Do not modify anything." -ApprovalMode plan
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Task,

    [string]$Model = "qwen3-coder:30b",

    [string]$WallTime = "10m",

    [int]$MaxToolCalls = 50,

    [int]$MaxSessionTurns = 40,

    [ValidateSet('auto','plan','default')]
    [string]$ApprovalMode = 'auto'
)

# Make sure qwen is on PATH. The official installer puts it at
# $env:LOCALAPPDATA\qwen-code\bin\qwen.cmd and adds that to user PATH, but
# processes spawned from this script may not see the user PATH yet, so
# we explicitly add the install dir to PATH for this run.
$qwenBin = Join-Path $env:LOCALAPPDATA 'qwen-code\bin'
if (Test-Path (Join-Path $qwenBin 'qwen.cmd')) {
    if (($env:Path -split ';') -notcontains $qwenBin) {
        $env:Path = "$qwenBin;$env:Path"
    }
}
if (-not (Get-Command qwen -ErrorAction SilentlyContinue)) {
    Write-Error "qwen CLI not found on PATH and not present at $qwenBin. See D:\AI Workflow RnD\Claude_Local_Qwen_Worker_Setup.txt step 4."
    exit 127
}

# Make sure Ollama is reachable; cheap pre-flight so we fail fast with a clear message.
try {
    $null = Invoke-WebRequest -Uri 'http://localhost:11434/api/tags' -UseBasicParsing -TimeoutSec 5
} catch {
    Write-Warning "Ollama at http://localhost:11434 did not respond. Is the Ollama app running?"
    Write-Warning "Continuing anyway; qwen will surface the error if Ollama is truly down."
}

& qwen -p $Task `
    --model $Model `
    --auth-type openai `
    --approval-mode $ApprovalMode `
    --max-wall-time $WallTime `
    --max-tool-calls $MaxToolCalls `
    --max-session-turns $MaxSessionTurns `
    --output-format json

exit $LASTEXITCODE
