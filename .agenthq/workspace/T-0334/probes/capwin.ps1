# T-0334 by-eye channel: raise a window and read the desktop over it IN ONE PROCESS.
#
# Why not the round-11 recipe (cap2.cs inside the editor)? Because this session's desktop is contended:
# a second agent is driving the Cupcakehour editor and takes the foreground every few seconds, and the
# Shaper editor's floating tool windows are hidden whenever that editor is not the active application.
# A capture issued as a separate `unity.exe command` round trip is 1-2 s after the raise, which is long
# enough to lose the race. Raising and grabbing in the same process closes that gap.
#
#   capwin.ps1 -Title "Mirage" -Out shots\mirage.png [-ProcId 8124] [-Sleep 500]
param([Parameter(Mandatory=$true)][string]$Title,
      [Parameter(Mandatory=$true)][string]$Out,
      [int]$ProcId = 8124,
      [int]$Sleep = 500)

$sig = @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public class Cap328 {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public static IntPtr Find(uint want, string title) {
    IntPtr hit = IntPtr.Zero;
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (pid != want) return true;
      var sb = new StringBuilder(512); GetWindowText(h, sb, 512);
      if (sb.ToString() == title) { hit = h; return false; }
      return true;
    }, IntPtr.Zero);
    return hit;
  }
  // A tool window Unity has only just created carries an EMPTY OS title until the app is activated,
  // so "the one visible window of this process that is not the main window" is the reliable fallback.
  public static IntPtr FindOther(uint want, IntPtr main) {
    IntPtr hit = IntPtr.Zero;
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (pid != want || h == main || !IsWindowVisible(h)) return true;
      hit = h; return false;
    }, IntPtr.Zero);
    return hit;
  }
  public static void Raise(IntPtr h) {
    IntPtr fg = GetForegroundWindow();
    uint p1; uint t1 = GetWindowThreadProcessId(fg, out p1);
    uint t2 = GetCurrentThreadId();
    AttachThreadInput(t1, t2, true);
    ShowWindow(h, 9);            // SW_RESTORE — SW_SHOW alone leaves a minimised window at -32000,-32000
    SetWindowPos(h, (IntPtr)(-1), 0, 0, 0, 0, 0x0013);  // HWND_TOPMOST, NOMOVE|NOSIZE|NOACTIVATE
    BringWindowToTop(h);
    SetForegroundWindow(h);
    AttachThreadInput(t1, t2, false);
  }
}
"@
if (-not ("Cap328" -as [type])) { Add-Type -TypeDefinition $sig }
Add-Type -AssemblyName System.Drawing
# without this the shell is DPI-virtualised: GetWindowRect and CopyFromScreen both come back at the
# scaled-down logical size, and a 12 px glyph does not survive that.
[void][Cap328]::SetProcessDPIAware()

$h = [Cap328]::Find([uint32]$ProcId, $Title)
if ($h -eq [IntPtr]::Zero) {
  # a hidden floating tool window is not enumerable while its app is in the background: raise the MAIN
  # editor window first, which shows every floating window the editor owns, then look again.
  $main = (Get-Process -Id $ProcId).MainWindowHandle
  [Cap328]::Raise($main); Start-Sleep -Milliseconds 1200
  $h = [Cap328]::Find([uint32]$ProcId, $Title)
  if ($h -eq [IntPtr]::Zero) { $h = [Cap328]::FindOther([uint32]$ProcId, $main) }
}
if ($h -eq [IntPtr]::Zero) { Write-Output "NOT FOUND: $Title"; exit 1 }

[Cap328]::Raise($h)
Start-Sleep -Milliseconds $Sleep
$r = New-Object Cap328+RECT
[void][Cap328]::GetWindowRect($h, [ref]$r)
$w = $r.R - $r.L; $ht = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap($w, $ht)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, (New-Object System.Drawing.Size($w, $ht)))
$dir = Split-Path -Parent $Out
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
$bmp.Save($Out)
$g.Dispose(); $bmp.Dispose()
# leave it non-topmost again so nothing is left pinned over the user's desktop
[void][Cap328]::SetWindowPos($h, [IntPtr](-2), 0, 0, 0, 0, 0x0013)
Write-Output ("wrote {0} {1}x{2} at {3},{4}" -f $Out, $w, $ht, $r.L, $r.T)
