// Find and screenshot the Laubrary Dev editor window via PrintWindow.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class CoplayProbe
{
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public static string Probe()
    {
        var sb = new StringBuilder();
        IntPtr bestHwnd = IntPtr.Zero;
        RECT bestRect = new RECT();
        int bestArea = 0;
        string bestTitle = "";

        EnumWindows((hWnd, lParam) =>
        {
            int len = GetWindowTextLength(hWnd);
            if (len <= 0) return true;
            var t = new StringBuilder(len + 1);
            GetWindowText(hWnd, t, t.Capacity);
            string title = t.ToString();
            if (!title.Contains("Laubrary Dev")) return true;
            if (!IsWindowVisible(hWnd)) return true;
            RECT r;
            GetWindowRect(hWnd, out r);
            int area = (r.Right - r.Left) * (r.Bottom - r.Top);
            uint pid; GetWindowThreadProcessId(hWnd, out pid);
            sb.AppendLine($"Laubrary Dev window: hwnd={hWnd} rect=({r.Left},{r.Top},{r.Right},{r.Bottom}) area={area} title={title}");
            if (area > bestArea) { bestArea = area; bestHwnd = hWnd; bestRect = r; bestTitle = title; }
            return true;
        }, IntPtr.Zero);

        if (bestHwnd == IntPtr.Zero) { sb.AppendLine("No Laubrary Dev window found"); return sb.ToString(); }

        // Try to print
        ShowWindow(bestHwnd, 9 /* SW_RESTORE */);
        SetForegroundWindow(bestHwnd);
        System.Threading.Thread.Sleep(500);

        int w = bestRect.Right - bestRect.Left;
        int h = bestRect.Bottom - bestRect.Top;
        if (w < 100 || h < 100) return sb.ToString() + " (window too small)";

        using (var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            IntPtr hdc = g.GetHdc();
            try { PrintWindow(bestHwnd, hdc, 2); }
            finally { g.ReleaseHdc(hdc); }
            string outPath = Path.Combine(Application.dataPath, "..", "Screenshots", "pyreplus_window_real.png");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
            sb.AppendLine($"Saved {w}x{h} to {outPath}");
        }
        return sb.ToString();
    }
}
