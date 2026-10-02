# T-0326 by-eye channel, variant B: PrintWindow instead of CopyFromScreen.
#
# capwin.ps1 reads the DESKTOP over a window's rect, so it captures whatever is on top there — and this
# editor's floating tool windows all sit at overlapping rects, so raising the target does not reliably win
# (measured: asking for "Choreographer" returned a picture of "Zoes"). PrintWindow asks the window to render
# ITSELF into a bitmap, so z-order does not matter and nothing has to be closed, moved or minimised.
# Flag 2 = PW_RENDERFULLCONTENT is required for Unity's GPU/DirectComposition windows (0 returns black).
#
#   capprint.ps1 -Title "Choreographer" -Out shots\choreo.png [-ProcId 8124]
param([Parameter(Mandatory=$true)][string]$Title,
      [Parameter(Mandatory=$true)][string]$Out,
      [int]$ProcId = 184580,
      [int]$Left = -99999,   # physical-pixel origin of the target window; used INSTEAD of the title when
      [int]$Top  = -99999)   # given, because a background editor's tool windows carry an empty OS title

$sig = @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public class Prn326 {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  public static void Raise(IntPtr h) {
    IntPtr fg = GetForegroundWindow();
    uint p1; uint t1 = GetWindowThreadProcessId(fg, out p1);
    uint t2 = GetCurrentThreadId();
    AttachThreadInput(t1, t2, true);
    ShowWindow(h, 9);
    BringWindowToTop(h);
    SetForegroundWindow(h);
    AttachThreadInput(t1, t2, false);
  }
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public static IntPtr Find(uint want, string title) {
    IntPtr hit = IntPtr.Zero;
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (pid != want || !IsWindowVisible(h)) return true;
      var sb = new StringBuilder(512); GetWindowText(h, sb, 512);
      if (sb.ToString() == title) { hit = h; return false; }
      return true;
    }, IntPtr.Zero);
    return hit;
  }
  public static IntPtr FindAt(uint want, int left, int top, int tol) {
    IntPtr hit = IntPtr.Zero;
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (pid != want || !IsWindowVisible(h)) return true;
      RECT r; GetWindowRect(h, out r);
      if (Math.Abs(r.L - left) <= tol && Math.Abs(r.T - top) <= tol) { hit = h; return false; }
      return true;
    }, IntPtr.Zero);
    return hit;
  }
  public static string List(uint want) {
    var sb2 = new StringBuilder();
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (pid != want) return true;
      var sb = new StringBuilder(512); GetWindowText(h, sb, 512);
      sb2.Append(IsWindowVisible(h) ? "[vis] " : "[hid] ").Append(sb.ToString()).Append("\n");
      return true;
    }, IntPtr.Zero);
    return sb2.ToString();
  }
}
"@
if (-not ("Prn326" -as [type])) { Add-Type -TypeDefinition $sig }
Add-Type -AssemblyName System.Drawing
[void][Prn326]::SetProcessDPIAware()

$h = [IntPtr]::Zero
if ($Left -ne -99999) { $h = [Prn326]::FindAt([uint32]$ProcId, $Left, $Top, 6) }
if ($h -eq [IntPtr]::Zero) { $h = [Prn326]::Find([uint32]$ProcId, $Title) }
if ($h -eq [IntPtr]::Zero) {
  # A floating tool window carries an EMPTY OS title while its app is in the background, so it cannot be
  # found by name until the editor is activated once. Activate the MAIN window, then look again — the grab
  # itself does not need the target on top, PrintWindow renders it wherever it is in the z-order.
  $main = (Get-Process -Id $ProcId).MainWindowHandle
  [Prn326]::Raise($main); Start-Sleep -Milliseconds 1200
  $h = [Prn326]::Find([uint32]$ProcId, $Title)
}
if ($h -eq [IntPtr]::Zero) {
  Write-Output "NOT FOUND: $Title"
  Write-Output ([Prn326]::List([uint32]$ProcId))
  exit 1
}
$r = New-Object Prn326+RECT
[void][Prn326]::GetWindowRect($h, [ref]$r)
$w = $r.R - $r.L; $ht = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap($w, $ht)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok = [Prn326]::PrintWindow($h, $hdc, 2)
$g.ReleaseHdc($hdc)
$dir = Split-Path -Parent $Out
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
$bmp.Save($Out)
$g.Dispose(); $bmp.Dispose()
Write-Output ("PrintWindow={0} wrote {1} {2}x{3} at {4},{5}" -f $ok, $Out, $w, $ht, $r.L, $r.T)
