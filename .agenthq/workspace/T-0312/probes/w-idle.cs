// Rebuilds the window named by T0312.unit at the pane/window widths in T0312.pane / T0312.winw, clears
// the console, and returns immediately; the caller then idles and reads LogEntries.GetCountsByType.
// This is T-0304's layout-struggle detector: a control whose size write depends on the size it produced
// re-lays out forever and floods the console while the editor sits idle.
string unit = UnityEditor.EditorPrefs.GetString("T0312.unit", "shaper");
float pane = float.Parse(UnityEditor.EditorPrefs.GetString("T0312.pane", "400"));
float winw = float.Parse(UnityEditor.EditorPrefs.GetString("T0312.winw", "1700"));
var win = ZWin(unit == "pyre" ? "PyreWindow" : "ShaperWindow");
if (win == null) return "NO WINDOW " + unit;
UnityEditor.EditorPrefs.SetFloat(unit == "pyre" ? "ZUI.Split.pyre.window.split.v1" : "ZUI.Split.shaper.window.split.v1", pane);
win.position = new UnityEngine.Rect(20, 20, winw, 1000);
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.MethodInfo rb = null;
for (var t = win.GetType(); t != null && rb == null; t = t.BaseType)
    rb = t.GetMethod("Rebuild", BFi | System.Reflection.BindingFlags.DeclaredOnly);
rb.Invoke(win, null); win.Repaint();
var lt = System.Type.GetType("UnityEditor.LogEntries,UnityEditor");
lt.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).Invoke(null, null);
return "set " + unit + " pane=" + pane + " win=" + winw;
