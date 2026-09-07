Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public static class PW {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public static System.Collections.Generic.List<IntPtr> Find(string tag) {
    var list = new System.Collections.Generic.List<IntPtr>();
    EnumWindows((h, l) => { var sb = new StringBuilder(512); GetWindowText(h, sb, 512); if (sb.ToString().Contains(tag)) list.Add(h); return true; }, IntPtr.Zero);
    return list;
  }
}
"@
[PW]::SetProcessDpiAwarenessContext([IntPtr]::new(-4)) | Out-Null
foreach ($pair in @(@("PyreProbeWin","window_pyre.png"), @("ZoeProbeWin","window_zoe.png"))) {
  $hs = [PW]::Find($pair[0])
  if ($hs.Count -eq 0) { Write-Output "$($pair[0]): not found"; continue }
  $h = $hs[0]; $r = New-Object PW+RECT; [PW]::GetWindowRect($h, [ref]$r) | Out-Null
  $w = $r.R - $r.L; $hh = $r.B - $r.T
  $bmp = New-Object System.Drawing.Bitmap $w, $hh
  $g = [System.Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
  $ok = [PW]::PrintWindow($h, $hdc, 2); $g.ReleaseHdc($hdc)
  $path = "D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0245\" + $pair[1]
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
  Write-Output "$($pair[0]): ${w}x${hh} ok=$ok -> $path"
}
