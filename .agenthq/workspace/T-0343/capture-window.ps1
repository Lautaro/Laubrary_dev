param(
    [Parameter(Mandatory)] [string] $Title,
    [Parameter(Mandatory)] [string] $Out,
    [string] $ProjectPath = 'D:\UNITY\Laubrary Dev'
)
# PrintWindow capture of one Unity editor window, matched by owning PID + exact title.
# Works when the window is covered by other windows; not when minimized.
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public static class W {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern IntPtr SetProcessDpiAwarenessContext(IntPtr v);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@
[W]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

$unityPid = (Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" |
    Where-Object { $_.CommandLine -match [regex]::Escape($ProjectPath) + '"?(\s|$)' } |
    Select-Object -First 1).ProcessId
if (-not $unityPid) { throw "No Unity.exe for $ProjectPath" }

$found = New-Object System.Collections.Generic.List[IntPtr]
$cb = [W+EnumProc]{ param($h, $l)
    $procId = 0; [W]::GetWindowThreadProcessId($h, [ref]$procId) | Out-Null
    if ($procId -eq $unityPid -and [W]::IsWindowVisible($h)) {
        $sb = New-Object System.Text.StringBuilder 256
        [W]::GetWindowText($h, $sb, 256) | Out-Null
        if ($sb.ToString() -eq $Title) { $found.Add($h) }
    }
    $true }
[W]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
if ($found.Count -eq 0) { throw "No visible window titled '$Title' in PID $unityPid" }

$h = $found[0]
$r = New-Object W+RECT; [W]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.R - $r.L; $hgt = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap $w, $hgt
$g = [System.Drawing.Graphics]::FromImage($bmp)
$dc = $g.GetHdc(); [W]::PrintWindow($h, $dc, 2) | Out-Null; $g.ReleaseHdc($dc); $g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
"captured PID=$unityPid hwnd=$h ${w}x${hgt} rect=($($r.L),$($r.T)) -> $Out"
