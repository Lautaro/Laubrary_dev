<#
  Forwarder to the one maintained Qwen helper: D:\Workstation\Qwen\qwen-delegate.ps1
  That copy refuses to load the 22 GB model when RAM is short (exit 75: do the task
  yourself), unloads it right after each call, and logs who used it and why.
  Same parameters as before; see that file for documentation.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)][string]$Task,
    [string]$Model,
    [string]$WallTime,
    [int]$MaxToolCalls,
    [int]$MaxSessionTurns,
    [ValidateSet('auto','plan','default')][string]$ApprovalMode
)
& 'D:\Workstation\Qwen\qwen-delegate.ps1' @PSBoundParameters
exit $LASTEXITCODE
