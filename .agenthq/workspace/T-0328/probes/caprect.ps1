# Grab a raw desktop rect WITHOUT touching focus — for a PopupWindow, which closes the instant anything
# else is activated. Coordinates are physical pixels.
param([int]$X, [int]$Y, [int]$W, [int]$H, [Parameter(Mandatory=$true)][string]$Out, [switch]$Raise, [int]$ProcId = 8124)
$sig = @"
using System;
using System.Runtime.InteropServices;
public class Rect325 {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int c);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a,uint b,bool at);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
}
"@
if (-not ("Rect325" -as [type])) { Add-Type -TypeDefinition $sig }
Add-Type -AssemblyName System.Drawing
[void][Rect325]::SetProcessDPIAware()
if ($Raise) {
  $m = (Get-Process -Id $ProcId).MainWindowHandle
  $fg = [Rect325]::GetForegroundWindow(); $p = 0
  $t1 = [Rect325]::GetWindowThreadProcessId($fg, [ref]$p); $t2 = [Rect325]::GetCurrentThreadId()
  [void][Rect325]::AttachThreadInput($t1, $t2, $true)
  [void][Rect325]::ShowWindow($m, 9); [void][Rect325]::BringWindowToTop($m); [void][Rect325]::SetForegroundWindow($m)
  [void][Rect325]::AttachThreadInput($t1, $t2, $false)
  Start-Sleep -Milliseconds 700
}
$bmp = New-Object System.Drawing.Bitmap($W, $H)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($X, $Y, 0, 0, (New-Object System.Drawing.Size($W, $H)))
$bmp.Save($Out); $g.Dispose(); $bmp.Dispose()
Write-Output ("wrote {0} {1}x{2} at {3},{4}" -f $Out, $W, $H, $X, $Y)
