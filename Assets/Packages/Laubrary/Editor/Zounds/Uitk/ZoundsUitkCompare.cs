using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Laubrary.Zounds.Uitk {

    /// <summary>
    /// The side-by-side check for the UI Toolkit port of the Zounds windows (T-0460): every piece of the new UI is
    /// verified against the old one by looking, not by reasoning about code. The owner's rule for this port is "same
    /// colors, sizes, shapes everything ... make sure visual confirmation is used", and a compile or a correct value has
    /// already, more than once this project, produced something that was wrong on screen.
    ///
    /// **What it does.** Puts the old (IMGUI) window and its new (UI Toolkit) twin at exactly the same size, lets both
    /// redraw, captures each window's client area from the screen as Windows draws it (works even when a window is
    /// behind another), and writes, per comparison: the old image, the new image, the pair side by side, and a
    /// difference image (red where any channel differs by more than <see cref="Tolerance"/>, the old image dimmed
    /// behind it), plus the share of differing pixels. The result is readable by eye in one picture.
    ///
    /// **Why capture from the screen rather than render offscreen.** The two technologies draw through different
    /// paths; only what finally reaches the window is the thing being compared.
    ///
    /// Windows only (it uses the operating system's window capture).
    /// </summary>
    public static class ZoundsUitkCompare {

        /// <summary>Per-channel difference (0-255) that still counts as the same pixel.</summary>
        public const int Tolerance = 8;

        public static string OutputFolder => Path.Combine(Path.GetTempPath(), "ZoundsUitkCompare");

        /// <summary>
        /// Lays out <paramref name="newWindow"/> at the same size as <paramref name="oldWindow"/>, right beside it, and
        /// after both have redrawn captures and compares them. Returns the report path at once; the report is written a
        /// few editor updates later. <paramref name="label"/> names the output files.
        /// </summary>
        public static string CompareLater(EditorWindow oldWindow, EditorWindow newWindow, string label, int settleFrames = 6) {
            Directory.CreateDirectory(OutputFolder);
            string report = Path.Combine(OutputFolder, label + ".txt");
            File.WriteAllText(report, "pending\n");
            var p = oldWindow.position;
            newWindow.position = new Rect(p.x + p.width + 12f, p.y, p.width, p.height);
            oldWindow.Repaint(); newWindow.Repaint();
            int frames = 0;
            EditorApplication.CallbackFunction tick = null;
            tick = () => {
                oldWindow.Repaint(); newWindow.Repaint();
                if (++frames < settleFrames) return;
                EditorApplication.update -= tick;
                try { File.WriteAllText(report, Compare(oldWindow, newWindow, label)); }
                catch (Exception e) { File.WriteAllText(report, "FAILED: " + e); }
            };
            EditorApplication.update += tick;
            return report;
        }

        /// <summary>Captures both windows now and writes the comparison. Returns the summary text.</summary>
        public static string Compare(EditorWindow oldWindow, EditorWindow newWindow, string label) {
            var a = Capture(oldWindow);
            var b = Capture(newWindow);
            if (a == null || b == null) return "FAILED: could not capture " + (a == null ? "the old window" : "the new window");
            int w = Mathf.Min(a.width, b.width), h = Mathf.Min(a.height, b.height);
            var pa = a.GetPixels32(); var pb = b.GetPixels32();
            var diff = new Color32[w * h];
            long differing = 0;
            for (int y = 0; y < h; y++) {
                for (int x = 0; x < w; x++) {
                    var ca = pa[y * a.width + x]; var cb = pb[y * b.width + x];
                    int d = Mathf.Max(Mathf.Abs(ca.r - cb.r), Mathf.Max(Mathf.Abs(ca.g - cb.g), Mathf.Abs(ca.b - cb.b)));
                    if (d > Tolerance) { differing++; diff[y * w + x] = new Color32(255, 40, 40, 255); }
                    else diff[y * w + x] = new Color32((byte)(ca.r / 4), (byte)(ca.g / 4), (byte)(ca.b / 4), 255);
                }
            }
            var dTex = new Texture2D(w, h, TextureFormat.RGBA32, false); dTex.SetPixels32(diff); dTex.Apply();
            var pair = new Texture2D(a.width + b.width + 8, Mathf.Max(a.height, b.height), TextureFormat.RGBA32, false);
            var fill = new Color32[pair.width * pair.height];
            for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(255, 0, 255, 255);
            pair.SetPixels32(fill);
            pair.SetPixels32(0, pair.height - a.height, a.width, a.height, pa);
            pair.SetPixels32(a.width + 8, pair.height - b.height, b.width, b.height, pb);
            pair.Apply();

            Directory.CreateDirectory(OutputFolder);
            string Save(Texture2D t, string suffix) { string f = Path.Combine(OutputFolder, label + "_" + suffix + ".png"); File.WriteAllBytes(f, t.EncodeToPNG()); return f; }
            Save(a, "old"); Save(b, "new"); Save(pair, "pair"); string df = Save(dTex, "diff");
            double pct = 100.0 * differing / Mathf.Max(1, w * h);
            var sb = new StringBuilder();
            sb.Append(label).Append(": ").Append(pct.ToString("0.00")).Append("% of pixels differ (tolerance ").Append(Tolerance).Append(")\n");
            sb.Append("old ").Append(a.width).Append('x').Append(a.height).Append(", new ").Append(b.width).Append('x').Append(b.height).Append('\n');
            sb.Append("images in ").Append(OutputFolder).Append(" (").Append(label).Append("_old/_new/_pair/_diff.png)\n");
            UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b);
            UnityEngine.Object.DestroyImmediate(dTex); UnityEngine.Object.DestroyImmediate(pair);
            return sb.ToString();
        }

        // ── capture ──

        /// <summary>The window's client area as Windows draws it, or null when it cannot be found.</summary>
        public static Texture2D Capture(EditorWindow window) {
            IntPtr hwnd = FindByTitle(window.titleContent.text);
            if (hwnd == IntPtr.Zero) return null;
            GetClientRect(hwnd, out RECT cr);
            GetWindowRect(hwnd, out RECT wr);
            var origin = new POINT { x = 0, y = 0 };
            ClientToScreen(hwnd, ref origin);
            int ox = origin.x - wr.left, oy = origin.y - wr.top;
            int ww = wr.right - wr.left, wh = wr.bottom - wr.top;
            int cw = cr.right - cr.left, ch = cr.bottom - cr.top;
            if (ww <= 0 || wh <= 0 || cw <= 0 || ch <= 0) return null;

            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr bmp = CreateCompatibleBitmap(screenDc, ww, wh);
            IntPtr old = SelectObject(memDc, bmp);
            PrintWindow(hwnd, memDc, 2);   // PW_RENDERFULLCONTENT: needed for GPU-drawn windows such as Unity's
            var bmi = new BITMAPINFO { biSize = 40, biWidth = ww, biHeight = wh, biPlanes = 1, biBitCount = 32, biCompression = 0 };
            var bgra = new byte[ww * wh * 4];
            SelectObject(memDc, old);
            GetDIBits(memDc, bmp, 0, (uint)wh, bgra, ref bmi, 0);
            DeleteObject(bmp); DeleteDC(memDc); ReleaseDC(IntPtr.Zero, screenDc);

            // Bottom-up BGRA of the whole window -> top-down client area -> Unity's bottom-up RGBA.
            var px = new Color32[cw * ch];
            for (int y = 0; y < ch; y++) {
                int srcRow = wh - 1 - (oy + y);            // DIB rows are bottom-up
                int dstRow = ch - 1 - y;                  // Texture2D rows are bottom-up
                for (int x = 0; x < cw; x++) {
                    int s = (srcRow * ww + ox + x) * 4;
                    px[dstRow * cw + x] = new Color32(bgra[s + 2], bgra[s + 1], bgra[s], 255);
                }
            }
            var tex = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
            tex.SetPixels32(px); tex.Apply();
            return tex;
        }

        private static IntPtr FindByTitle(string title) {
            IntPtr found = IntPtr.Zero;
            uint pid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            EnumWindows((h, l) => {
                GetWindowThreadProcessId(h, out uint wp);
                if (wp != pid || !IsWindowVisible(h)) return true;
                var sb = new StringBuilder(512);
                GetWindowText(h, sb, sb.Capacity);
                if (sb.ToString() == title) { found = h; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private delegate bool EnumProc(IntPtr h, IntPtr l);
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int left, top, right, bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFO {
            public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter;
            public uint biClrUsed, biClrImportant;
        }
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc p, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h, ref POINT p);
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, byte[] bits, ref BITMAPINFO bmi, uint usage);
    }
}
