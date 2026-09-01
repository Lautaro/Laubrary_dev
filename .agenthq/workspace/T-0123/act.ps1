param([string]$Ops)
. "D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0123\win32.ps1"
Add-Type -AssemblyName System.Windows.Forms
$h = Find-Win "Chunks Mock"
if ($h -eq [IntPtr]::Zero) { "ERR: window not found"; exit 1 }
foreach ($line in (Get-Content -LiteralPath $Ops)) {
    $l = $line.Trim()
    if ($l -eq "" -or $l.StartsWith("#")) { continue }
    $p = $l -split '\s+', 2
    $cmd = $p[0]
    $arg = if ($p.Count -gt 1) { $p[1] } else { "" }
    switch ($cmd) {
        "focus"  { [void][W]::SetForegroundWindow($h); Start-Sleep -Milliseconds 350; "focus fg=$([W]::GetForegroundWindow()) target=$h" }
        "click"  { $a = $arg -split '\s+'; ClickAt ([int]$a[0]) ([int]$a[1]); "click $($a[0]),$($a[1])" }
        "drag"   { $a = $arg -split '\s+'; DragFromTo ([int]$a[0]) ([int]$a[1]) ([int]$a[2]) ([int]$a[3]); "drag $arg" }
        "move"   { $a = $arg -split '\s+'; MoveTo ([int]$a[0]) ([int]$a[1]); "move $arg" }
        "shot"   { Start-Sleep -Milliseconds 250; Shot $h $arg }
        "type"   { [System.Windows.Forms.SendKeys]::SendWait($arg); Start-Sleep -Milliseconds 250; "type $arg" }
        "sleep"  { Start-Sleep -Milliseconds ([int]$arg); "sleep $arg" }
        default  { "ERR unknown op: $l" }
    }
}
