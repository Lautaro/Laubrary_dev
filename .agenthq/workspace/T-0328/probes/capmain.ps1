# T-0328 by-eye channel, variant C: capture the MAIN editor window via PrintWindow and crop a docked
# pane out of it by its LOGICAL rect.
#
# Round 17's cap.sh finds a FLOATING tool window by its physical rect. Every window this round has to
# photograph is DOCKED, so they all share one rect (0,26) and one HWND — the main window. The pane is
# therefore cropped out of a main-window grab instead, using the same measured mapping
# (physical = 2.25*logical - (5,45), pixelsPerPoint 2.25) expressed relative to the window's own origin.
#
#   capmain.ps1 -Out shots\x.png [-ProcId 8124] [-LX 0 -LY 26 -LW 1068 -LH 783]
param([Parameter(Mandatory=$true)][string]$Out,
      [int]$ProcId = 8124,
      [double]$LX = -1, [double]$LY = -1, [double]$LW = -1, [double]$LH = -1,
      [double]$Ppp = 2.25)

$sig = @"
using System;
using System.Runtime.InteropServices;
public class PrnMain328 {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@
if (-not ("PrnMain328" -as [type])) { Add-Type -TypeDefinition $sig }
Add-Type -AssemblyName System.Drawing
[void][PrnMain328]::SetProcessDPIAware()

$h = (Get-Process -Id $ProcId).MainWindowHandle
if ($h -eq [IntPtr]::Zero) { Write-Output "NO MAIN WINDOW for pid $ProcId"; exit 1 }
$r = New-Object PrnMain328+RECT
[void][PrnMain328]::GetWindowRect($h, [ref]$r)
$w = $r.R - $r.L; $ht = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap($w, $ht)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok = [PrnMain328]::PrintWindow($h, $hdc, 2)
$g.ReleaseHdc($hdc); $g.Dispose()

$dir = Split-Path -Parent $Out
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }

if ($LW -gt 0) {
  # logical -> physical screen -> offset into the window's own bitmap
  $px = [int][Math]::Round($Ppp * $LX) - 5 - $r.L
  $py = [int][Math]::Round($Ppp * $LY) - 45 - $r.T
  $pw = [int][Math]::Round($Ppp * $LW)
  $ph = [int][Math]::Round($Ppp * $LH)
  if ($px -lt 0) { $pw += $px; $px = 0 }
  if ($py -lt 0) { $ph += $py; $py = 0 }
  if ($px + $pw -gt $w)  { $pw = $w  - $px }
  if ($py + $ph -gt $ht) { $ph = $ht - $py }
  $crop = New-Object System.Drawing.Bitmap($pw, $ph)
  $g2 = [System.Drawing.Graphics]::FromImage($crop)
  $g2.DrawImage($bmp, (New-Object System.Drawing.Rectangle(0,0,$pw,$ph)), (New-Object System.Drawing.Rectangle($px,$py,$pw,$ph)), [System.Drawing.GraphicsUnit]::Pixel)
  $g2.Dispose()
  $crop.Save($Out); $crop.Dispose()
  Write-Output ("PrintWindow={0} wrote {1} crop {2}x{3} from window {4}x{5} at {6},{7}" -f $ok, $Out, $pw, $ph, $w, $ht, $r.L, $r.T)
} else {
  $bmp.Save($Out)
  Write-Output ("PrintWindow={0} wrote {1} {2}x{3} at {4},{5}" -f $ok, $Out, $w, $ht, $r.L, $r.T)
}
$bmp.Dispose()
