param([string]$Title = "Chunks Mock", [string]$Out = "shot.png")

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class Win32Cap {
    [DllImport("user32.dll")] public static extern IntPtr SetProcessDpiAwarenessContext(IntPtr v);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

# -4 == DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2. Without it GetWindowRect returns virtualised
# coordinates while PrintWindow renders at physical resolution, and the capture comes out cropped.
[void][Win32Cap]::SetProcessDpiAwarenessContext([IntPtr](-4))
Add-Type -AssemblyName System.Drawing

$found = [IntPtr]::Zero
$cb = [Win32Cap+EnumWindowsProc]{
    param($h, $l)
    if (-not [Win32Cap]::IsWindowVisible($h)) { return $true }
    $len = [Win32Cap]::GetWindowTextLength($h)
    if ($len -le 0) { return $true }
    $sb = New-Object System.Text.StringBuilder ($len + 1)
    [void][Win32Cap]::GetWindowText($h, $sb, $sb.Capacity)
    if ($sb.ToString() -like "*$Title*") { $script:found = $h; return $false }
    return $true
}
[void][Win32Cap]::EnumWindows($cb, [IntPtr]::Zero)

if ($script:found -eq [IntPtr]::Zero) { Write-Output "NOT_FOUND"; exit 1 }

$r = New-Object Win32Cap+RECT
[void][Win32Cap]::GetWindowRect($script:found, [ref]$r)
$w = $r.Right - $r.Left
$h = $r.Bottom - $r.Top
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[void][Win32Cap]::PrintWindow($script:found, $hdc, 2)   # 2 == PW_RENDERFULLCONTENT
$g.ReleaseHdc($hdc)
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "OK $Out ${w}x${h}"
