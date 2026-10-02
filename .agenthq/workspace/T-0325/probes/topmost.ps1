# T-0325 by-eye channel helper.
# The desktop read only sees the TOP window at a rect. Round 11 minimised whatever sat above Unity;
# this session's obstructions are another Unity editor (Cupcakehour) and a Windows Terminal running
# a live session the user is typing in, so minimising is not acceptable. Making the TARGET window
# temporarily topmost is the reversible equivalent and disturbs nothing.
#   topmost.ps1 -Title "Mirage" -On          → that floating tool window jumps above everything
#   topmost.ps1 -Title "Mirage" -On:$false   → put it back
#   topmost.ps1 -List                        → every top-level window of the Shaper editor process
param([string]$Title = "", [switch]$On, [switch]$List, [int]$ProcId = 8124)

$sig = @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public class T325 {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  public static System.Collections.Generic.List<string> Find(uint want) {
    var res = new System.Collections.Generic.List<string>();
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (pid != want) return true;
      if (!IsWindowVisible(h)) return true;
      var sb = new StringBuilder(512); GetWindowText(h, sb, 512);
      res.Add(h.ToInt64() + "\t" + sb.ToString());
      return true;
    }, IntPtr.Zero);
    return res;
  }
}
"@
if (-not ("T325" -as [type])) { Add-Type -TypeDefinition $sig }

$rows = [T325]::Find([uint32]$ProcId)
if ($List) { $rows | ForEach-Object { $_ }; exit }

$hit = $rows | Where-Object { ($_ -split "`t")[1] -eq $Title }
if (-not $hit) { Write-Output "NOT FOUND: $Title"; $rows | ForEach-Object { $_ }; exit 1 }
$h = [IntPtr][int64](($hit | Select-Object -First 1) -split "`t")[0]
$after = if ($On) { [IntPtr](-1) } else { [IntPtr](-2) }   # HWND_TOPMOST / HWND_NOTOPMOST
[void][T325]::SetWindowPos($h, $after, 0, 0, 0, 0, 0x0013) # NOMOVE|NOSIZE|NOACTIVATE
Write-Output ("{0} topmost={1} hwnd={2}" -f $Title, [bool]$On, $h)
