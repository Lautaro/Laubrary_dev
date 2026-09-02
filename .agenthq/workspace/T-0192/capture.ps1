param([string]$Title, [string]$Out)
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class W {
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
[void][W]::SetProcessDpiAwarenessContext([IntPtr](-4))
Add-Type -AssemblyName System.Drawing

function Find-Win([string]$T) {
    $script:found = [IntPtr]::Zero
    $cb = [W+EnumWindowsProc]{
        param($h, $l)
        if (-not [W]::IsWindowVisible($h)) { return $true }
        $len = [W]::GetWindowTextLength($h)
        if ($len -le 0) { return $true }
        $sb = New-Object System.Text.StringBuilder ($len + 1)
        [void][W]::GetWindowText($h, $sb, $sb.Capacity)
        if ($sb.ToString() -eq $T) { $script:found = $h; return $false }
        return $true
    }
    [void][W]::EnumWindows($cb, [IntPtr]::Zero)
    return $script:found
}

$h = Find-Win $Title
if ($h -eq [IntPtr]::Zero) { Write-Output "NOT_FOUND:$Title"; exit 1 }
$r = New-Object W+RECT
[void][W]::GetWindowRect($h, [ref]$r)
$w = $r.Right - $r.Left; $ht = $r.Bottom - $r.Top
$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[void][W]::PrintWindow($h, $hdc, 2)
$g.ReleaseHdc($hdc); $g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "$Out ${w}x${ht}"
