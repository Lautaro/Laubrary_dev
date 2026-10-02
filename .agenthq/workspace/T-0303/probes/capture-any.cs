using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEditor;

public static class AnyCap
{
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr v);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    struct RECT { public int L,T,R,B; }
    static EnumProc _proc; static IntPtr _found;

    static Type FindType(string n){ foreach(var a in AppDomain.CurrentDomain.GetAssemblies()){ Type[] ts; try { ts = a.GetTypes(); } catch { continue; } foreach(var t in ts) if(t.Name==n) return t; } return null; }

    static EditorWindow Find(string typeName)
    {
        var t = FindType(typeName);
        if (t == null) return null;
        foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>()) if (w != null && w.GetType() == t) return w;
        return null;
    }

    /// Tag the window (its OS title is what the capture searches for). Call this, then Snap in a SECOND call.
    public static string Tag()
    {
        var w = Find(EditorPrefs.GetString("A25.type", ""));
        if (w == null) return "no window of type " + EditorPrefs.GetString("A25.type", "");
        w.titleContent = new GUIContent("AnyCapTag");
        float ww = EditorPrefs.GetFloat("A25.capW", 1500f), hh = EditorPrefs.GetFloat("A25.capH", 1100f);
        w.position = new Rect(50, 50, ww, hh);
        w.Show(); w.Repaint();
        return "tagged " + w.GetType().Name + " at " + w.position.width + "x" + w.position.height;
    }

    public static string TagAndSnap()
    {
        var w = Find(EditorPrefs.GetString("A25.type", ""));
        if (w == null) return "no window";
        w.titleContent = new GUIContent("AnyCapTag");
        w.Show(); w.Focus(); w.Repaint();
        EditorApplication.delayCall += () => { EditorApplication.delayCall += () => { var r = Snap(); Debug.Log("AnyCap: " + r); }; };
        return "scheduled";
    }

    public static string Titles()
    {
        var all = new StringBuilder();
        _proc = (h, l) => { if (!IsWindowVisible(h)) return true; var sb = new StringBuilder(256); GetWindowText(h, sb, 256); var s = sb.ToString(); if (!string.IsNullOrEmpty(s)) all.Append('[').Append(s).Append(']'); return true; };
        EnumWindows(_proc, IntPtr.Zero);
        return all.ToString();
    }

    public static string Snap()
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        _found = IntPtr.Zero;
        _proc = (h, l) => { if (!IsWindowVisible(h)) return true; var sb = new StringBuilder(256); GetWindowText(h, sb, 256); if (sb.ToString().Contains("AnyCapTag")) { _found = h; return false; } return true; };
        EnumWindows(_proc, IntPtr.Zero);
        if (_found == IntPtr.Zero) return "no hwnd";
        GetWindowRect(_found, out var r); int w = r.R - r.L, h2 = r.B - r.T;
        var asm = System.Reflection.Assembly.Load("System.Drawing");
        var bmpT = asm.GetType("System.Drawing.Bitmap"); var gT = asm.GetType("System.Drawing.Graphics");
        var bmp = Activator.CreateInstance(bmpT, new object[]{ w, h2 });
        var g = gT.GetMethod("FromImage").Invoke(null, new[]{ bmp });
        var hdc = (IntPtr)gT.GetMethod("GetHdc").Invoke(g, null);
        PrintWindow(_found, hdc, 2);
        gT.GetMethod("ReleaseHdc", new[]{ typeof(IntPtr) }).Invoke(g, new object[]{ hdc });
        string path = @"C:\Users\Lauta\AppData\Local\Temp\claude\D--UNITY-Laubrary-Dev---Shaper\b6197aa9-96a6-43f3-921b-83cf03d08706\scratchpad\shots\a25-" + EditorPrefs.GetString("A25.capName","x") + "-" + DateTime.Now.ToString("HHmmss") + ".png";
        bmpT.GetMethod("Save", new[]{ typeof(string) }).Invoke(bmp, new object[]{ path });
        return "saved " + path + " " + w + "x" + h2;
    }

    /// Put the window's real title back.
    public static string Untag()
    {
        var w = Find(EditorPrefs.GetString("A25.type", ""));
        if (w == null) return "none";
        w.titleContent = new GUIContent(EditorPrefs.GetString("A25.title", "Window"));
        w.Repaint();
        return "untagged";
    }
}
