. "D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0123\win32.ps1"
$h = Find-Win "Chunks Mock"
"handle=$h"
$r = New-Object W+RECT
[void][W]::GetWindowRect($h, [ref]$r)
"windowRect L=$($r.Left) T=$($r.Top) R=$($r.Right) B=$($r.Bottom) W=$($r.Right-$r.Left) H=$($r.Bottom-$r.Top)"
$c = New-Object W+RECT
[void][W]::GetClientRect($h, [ref]$c)
"clientRect W=$($c.Right-$c.Left) H=$($c.Bottom-$c.Top)"
$p = New-Object W+POINT
$p.X = 0; $p.Y = 0
[void][W]::ClientToScreen($h, [ref]$p)
"clientOriginScreen X=$($p.X) Y=$($p.Y)"
