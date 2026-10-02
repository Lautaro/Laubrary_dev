// T-0313 — the Bake box's Destination readout at the narrowest the box can be.  The Bake box lives in
// Shaper's RIGHT pane (ShaperWindow.cs:1400), whose minWidth is 260 (ShaperWindow.cs:321), so the width
// that matters is the right pane's minimum, not the dial pane's.  Reports the row, the "Destination"
// label and the path Text: what each needs, what each has, and whether the path is drawn whole.
// Prefs: T313.winw (window width), T313.pane (split intent — a big value drives the right pane to its
// minimum), T313.doc (which document to bind; default the committed demo document).
float winw = float.Parse(UnityEditor.EditorPrefs.GetString("T313.winw", "900"));
float pane = float.Parse(UnityEditor.EditorPrefs.GetString("T313.pane", "1500"));
string docPath = UnityEditor.EditorPrefs.GetString("T313.doc", "Assets/Demos/ShaperDemo/ShaperDemoDoc.asset");
var sb = new System.Text.StringBuilder();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;

UnityEditor.EditorPrefs.SetFloat("ZUI.Split.shaper.window.split.v1", pane);
var win = ZWin("ShaperWindow");
if (win == null) { UnityEditor.EditorApplication.ExecuteMenuItem("Laubrary/Shaper"); win = ZWin("ShaperWindow"); }
if (win == null) return "NO SHAPER WINDOW";
win.position = new UnityEngine.Rect(5, 20, winw, 1000);
win.Show();
var doc = UnityEditor.AssetDatabase.LoadMainAssetAtPath(docPath);
if (doc == null) return "NO DOCUMENT at " + docPath;
System.Reflection.MethodInfo setAsset = null;
for (var t = win.GetType(); t != null && setAsset == null; t = t.BaseType)
    setAsset = t.GetMethod("SetAsset", BFi | System.Reflection.BindingFlags.DeclaredOnly);
setAsset.Invoke(win, new object[] { doc });
System.Reflection.MethodInfo rebuild = null;
for (var t = win.GetType(); t != null && rebuild == null; t = t.BaseType)
    rebuild = t.GetMethod("Rebuild", BFi | System.Reflection.BindingFlags.DeclaredOnly);
rebuild.Invoke(win, null); win.Repaint();

sb.Append("dataPath=").Append(UnityEngine.Application.dataPath)
  .Append(" doc=").Append(doc.name).Append(" (").Append(docPath).Append(")")
  .Append(" window=").Append(winw.ToString("F0")).Append(" splitIntent=").Append(pane.ToString("F0")).Append("\n");
return sb.ToString();
