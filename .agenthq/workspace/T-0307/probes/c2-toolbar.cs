// Measures the asset toolbar row at the declared minimum window (820x520): every child's x extent, the
// row's own width, and how much slack there is before a control would leave the window.
var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
float w = float.Parse(UnityEditor.EditorPrefs.GetString("T0307.winw", "820"));
win.position = new UnityEngine.Rect(60, 60, w, 520);
win.Show(); win.Repaint();
System.Reflection.MethodInfo rebuild = null;
for (var t = win.GetType(); t != null && rebuild == null; t = t.BaseType) rebuild = t.GetMethod("Rebuild", BFi | System.Reflection.BindingFlags.DeclaredOnly);
rebuild.Invoke(win, null); win.Repaint();
return "sized to " + win.position.width + "x" + win.position.height;
