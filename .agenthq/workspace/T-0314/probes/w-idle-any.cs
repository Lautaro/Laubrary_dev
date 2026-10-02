// T-0314 — T-0304's layout-struggle detector aimed at ANY window (T313.win) at width T313.width: resize,
// rebuild, clear the console, return.  The caller then idles and reads the counts with T-0312's w-count.cs.
// Needed because sweep-idle.sh only knows Pyre and Shaper, and the section bar this task changed lives in
// Chunks too — a bar that wraps is exactly the shape of control that could re-lay-out forever.
string wname = UnityEditor.EditorPrefs.GetString("T313.win", "ChunkWindow");
float width = float.Parse(UnityEditor.EditorPrefs.GetString("T313.width", "820"));
var win = ZWin(wname);
if (win == null) return "NO WINDOW " + wname;
win.position = new UnityEngine.Rect(20, 20, width, 1000);
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.MethodInfo rb = null;
for (var t = win.GetType(); t != null && rb == null; t = t.BaseType)
    rb = t.GetMethod("Rebuild", BFi | System.Reflection.BindingFlags.DeclaredOnly);
if (rb != null) rb.Invoke(win, null);
win.Repaint();
var lt = System.Type.GetType("UnityEditor.LogEntries,UnityEditor");
lt.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).Invoke(null, null);
return "set " + wname + " width=" + width;
