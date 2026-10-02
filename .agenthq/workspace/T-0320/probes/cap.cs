// T-0320 by-eye capture.  Two channels in one file, chosen by EditorPrefs "T320.capMode":
//   "print"  PrintWindow(hwnd,hdc,2)     - captures the window even when occluded, but returned a blank
//                                          client area in T-0318's session; re-tested here.
//   "screen" Graphics.CopyFromScreen()   - reads the composited desktop over the window's rect, so the
//                                          window must be visible and unobstructed.  This is what produced
//                                          the readable shots earlier in this programme.
// Target window is found by its title, EditorPrefs "T320.capTag"; the PNG goes to "T320.capOut".
using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEditor;

public static class T320Cap
{
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr v);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int c);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    struct RECT { public int L, T, R, B; }
    static EnumProc _proc; static IntPtr _found; static string _tag;

    public static string Execute()
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        _tag = EditorPrefs.GetString("T320.capTag", "T320Tag");
        string mode = EditorPrefs.GetString("T320.capMode", "screen");
        string outPath = EditorPrefs.GetString("T320.capOut", @"C:\Users\Lauta\AppData\Local\Temp\claude\D--UNITY-Laubrary-Dev---Shaper\b6197aa9-96a6-43f3-921b-83cf03d08706\scratchpad\shots\t320.png");
        _found = IntPtr.Zero;
        var seen = new StringBuilder();
        _proc = (h, l) =>
        {
            if (!IsWindowVisible(h)) return true;
            var t = new StringBuilder(256); GetWindowText(h, t, 256);
            string s = t.ToString();
            if (s.Contains(_tag)) { _found = h; GetWindowRect(h, out var rr); seen.Append("HIT '").Append(s).Append("' ").Append(rr.L).Append(',').Append(rr.T).Append(',').Append(rr.R).Append(',').Append(rr.B).Append('\n'); return false; }
            return true;
        };
        EnumWindows(_proc, IntPtr.Zero);
        if (_found == IntPtr.Zero) return "NO WINDOW titled '" + _tag + "'";
        if (mode == "screen") { ShowWindow(_found, 5); BringWindowToTop(_found); SetForegroundWindow(_found); }
        GetWindowRect(_found, out var r);
        int wd = r.R - r.L, ht = r.B - r.T;
        if (wd <= 0 || ht <= 0) return "bad rect " + r.L + "," + r.T + "," + r.R + "," + r.B;
        var asm = System.Reflection.Assembly.Load("System.Drawing");
        var bmpT = asm.GetType("System.Drawing.Bitmap");
        var gT = asm.GetType("System.Drawing.Graphics");
        var szT = asm.GetType("System.Drawing.Size");
        var bmp = Activator.CreateInstance(bmpT, new object[] { wd, ht });
        var g = gT.GetMethod("FromImage").Invoke(null, new[] { bmp });
        if (mode == "print")
        {
            var hdc = (IntPtr)gT.GetMethod("GetHdc").Invoke(g, null);
            PrintWindow(_found, hdc, 2);
            gT.GetMethod("ReleaseHdc", new[] { typeof(IntPtr) }).Invoke(g, new object[] { hdc });
        }
        else
        {
            var size = Activator.CreateInstance(szT, new object[] { wd, ht });
            gT.GetMethod("CopyFromScreen", new[] { typeof(int), typeof(int), typeof(int), typeof(int), szT })
              .Invoke(g, new object[] { r.L, r.T, 0, 0, size });
        }
        bmpT.GetMethod("Save", new[] { typeof(string) }).Invoke(bmp, new object[] { outPath });
        return seen + "wrote " + outPath + " " + wd + "x" + ht + " mode=" + mode;
    }
}
