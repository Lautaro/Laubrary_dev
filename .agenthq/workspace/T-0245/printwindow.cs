// PrintWindow both probe windows to PNG (DPI-aware; PW_RENDERFULLCONTENT).
var sb = new System.Text.StringBuilder();
var found = new System.Collections.Generic.Dictionary<string, System.IntPtr>();
Laubrary.Zui.ZuiVectorMarker.Data dummy;   // (keeps the Zui assembly referenced)
System.Type nativeT = null;
// Build the P/Invoke surface with a dynamically compiled helper via System.Runtime.InteropServices in a nested static class is not possible in a method body, so use Marshal.GetDelegateForFunctionPointer against user32/gdi32 exports.
var user32 = LoadLib("user32.dll"); var gdi = LoadLib("gdi32.dll");
System.IntPtr LoadLib(string n) { return System.Runtime.InteropServices.NativeLibrary.Load(n); }
System.IntPtr P(System.IntPtr lib, string name) { return System.Runtime.InteropServices.NativeLibrary.GetExport(lib, name); }
var setDpi = System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer<SetDpiFn>(P(user32, "SetProcessDpiAwarenessContext"));
setDpi(new System.IntPtr(-4));
var enumWindows = System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer<EnumWindowsFn>(P(user32, "EnumWindows"));
var getText = System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer<GetWindowTextFn>(P(user32, "GetWindowTextW"));
var isVisible = System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer<IsWindowVisibleFn>(P(user32, "IsWindowVisible"));
var getRect = System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer<GetWindowRectFn>(P(user32, "GetWindowRect"));
var printWindow = System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer<PrintWindowFn>(P(user32, "PrintWindow"));
EnumProc proc = (h, l) => { var s = new System.Text.StringBuilder(512); getText(h, s, 512); var t = s.ToString(); if (isVisible(h) && (t.Contains("PyreProbeWin") || t.Contains("ZoeProbeWin"))) found[t.Contains("Pyre") ? "pyre" : "zoe"] = h; return true; };
enumWindows(proc, System.IntPtr.Zero);
var drawing = System.Reflection.Assembly.Load("System.Drawing");
var bmpT = drawing.GetType("System.Drawing.Bitmap"); var gT = drawing.GetType("System.Drawing.Graphics"); var fmtT = drawing.GetType("System.Drawing.Imaging.ImageFormat");
foreach (var kv in found)
{
    RECT r; getRect(kv.Value, out r); int w = r.R - r.L, h = r.B - r.T;
    var bmp = System.Activator.CreateInstance(bmpT, new object[] { w, h });
    var g = gT.GetMethod("FromImage", new[] { drawing.GetType("System.Drawing.Image") }).Invoke(null, new[] { bmp });
    var hdc = (System.IntPtr)gT.GetMethod("GetHdc").Invoke(g, null);
    bool ok = printWindow(kv.Value, hdc, 2);
    gT.GetMethod("ReleaseHdc", new[] { typeof(System.IntPtr) }).Invoke(g, new object[] { hdc });
    string path = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0245\window_" + kv.Key + ".png";
    bmpT.GetMethod("Save", new[] { typeof(string), fmtT }).Invoke(bmp, new[] { path, fmtT.GetProperty("Png").GetValue(null) });
    ((System.IDisposable)g).Dispose(); ((System.IDisposable)bmp).Dispose();
    sb.AppendLine(kv.Key + " " + w + "x" + h + " ok=" + ok + " -> " + path);
}
return sb.Length == 0 ? "no windows found" : sb.ToString();
[System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Winapi)] delegate bool SetDpiFn(System.IntPtr ctx);
delegate bool EnumProc(System.IntPtr h, System.IntPtr l);
[System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Winapi)] delegate bool EnumWindowsFn(EnumProc p, System.IntPtr l);
[System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Winapi, CharSet = System.Runtime.InteropServices.CharSet.Unicode)] delegate int GetWindowTextFn(System.IntPtr h, System.Text.StringBuilder s, int n);
[System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Winapi)] delegate bool IsWindowVisibleFn(System.IntPtr h);
[System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Winapi)] delegate bool GetWindowRectFn(System.IntPtr h, out RECT r);
[System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Winapi)] delegate bool PrintWindowFn(System.IntPtr h, System.IntPtr hdc, uint f);
struct RECT { public int L, T, R, B; }
