var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var pyreT = FT("PyreWindow");
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;

// unbind both windows from every audit asset, and put Pyre's pane width back to its declared default
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{
    if (w == null) continue;
    var tn = w.GetType().Name;
    if (tn != "PyreWindow" && tn != "ShaperWindow") continue;
    if (tn == "PyreWindow") { var f = pyreT.GetField("leftPaneWidth", BFi); if (f != null) f.SetValue(w, 360f); }
    System.Reflection.MethodInfo sa = null;
    for (var t = w.GetType(); t != null && sa == null; t = t.BaseType) sa = t.GetMethod("SetAsset", BFi | System.Reflection.BindingFlags.DeclaredOnly);
    if (sa != null) { try { sa.Invoke(w, new object[] { null }); } catch (System.Exception) { } }
    w.Close();
    sb.Append("closed ").Append(tn).Append("\n");
}
UnityEditor.Undo.ClearAll();

// every asset and folder this pass created
string[] assets = {
  "Assets/Shaper/AuditA24a.asset", "Assets/Shaper/AuditA24b.asset", "Assets/Shaper/AuditA24g.asset",
  "Assets/Shaper/AuditA24u.asset", "Assets/Shaper/AuditA24v.asset", "Assets/Shaper/AuditA24Doc.asset",
  "Assets/Shaper/AuditA24Pyre.asset", "Assets/Shaper/AuditA24Pyre2.asset",
  "Assets/Shaper/AuditT0278Rect.asset", "Assets/Shaper/AuditT0278Star.asset",
  "Assets/Shaper/ShaperViews.asset",
  "Assets/Shaper/Audit0271", "Assets/Shaper/Audit0277", "Assets/Shaper/AuditA19"
};
foreach (var p in assets)
{
    bool existed = UnityEditor.AssetDatabase.LoadMainAssetAtPath(p) != null || UnityEditor.AssetDatabase.IsValidFolder(p);
    bool ok = UnityEditor.AssetDatabase.DeleteAsset(p);
    if (existed || ok) sb.Append("deleted ").Append(p).Append(" -> ").Append(ok).Append("\n");
}
UnityEditor.AssetDatabase.Refresh();

// EditorPrefs / SessionState this pass wrote
string[] prefs = { "A24.savedSel", "ZUI.Split.shaper.window.split.v1", "Shaper.lastView", "ShaperCap.name",
                   "ShaperCap.savedSel", "A24.dupPath", "A19.dupPath", "A19.srcPath", "Pyre.lastView" };
foreach (var k in prefs) if (UnityEditor.EditorPrefs.HasKey(k)) { UnityEditor.EditorPrefs.DeleteKey(k); sb.Append("pref removed ").Append(k).Append("\n"); }
foreach (var k in new string[] { "A24.samples", "A24.t0296", "A24.pyresplit", "A24.t0297host" })
    UnityEditor.SessionState.EraseString(k);

sb.Append("toggle bar userSel now = '").Append(UnityEditor.EditorPrefs.GetString("ZuiSectionToggleBar.ShaperWindow.userSel", "<none>")).Append("'\n");
return sb.ToString();
