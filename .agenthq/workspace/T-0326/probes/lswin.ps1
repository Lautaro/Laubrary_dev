param([int]$ProcId = 8124)
$sig = @"
using System; using System.Text; using System.Runtime.InteropServices;
public class LS326 {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public static string List(uint want) {
    var o = new StringBuilder();
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (pid != want || !IsWindowVisible(h)) return true;
      RECT r; GetWindowRect(h, out r);
      o.Append(h.ToInt64()).Append(" ").Append(r.L).Append(",").Append(r.T).Append(" ").Append(r.R-r.L).Append("x").Append(r.B-r.T).Append("\n");
      return true;
    }, IntPtr.Zero);
    return o.ToString();
  }
}
"@
if (-not ("LS326" -as [type])) { Add-Type -TypeDefinition $sig }
[void][LS326]::SetProcessDPIAware()
[LS326]::List($ProcId)
