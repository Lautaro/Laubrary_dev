var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
System.Func<string, System.Type> FindType = n => {
    foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
        foreach (var t in a.GetTypes()) if (t.Name == n) return t;
    return null;
};
var pyreT = FindType("PyreWindow");

// close every Pyre window, reopen through its own menu item, then bind each fixture
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
    if (w0 != null && w0.GetType() == pyreT) w0.Close();
foreach (var m in pyreT.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
    if (m.GetCustomAttributes(typeof(UnityEditor.MenuItem), false).Length > 0 && m.GetParameters().Length == 0) m.Invoke(null, null);
UnityEditor.EditorWindow win = null;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
    if (w0 != null && w0.GetType() == pyreT) win = w0;

System.Reflection.MethodInfo setAsset = null, rebuild = null;
for (var t = pyreT; t != null; t = t.BaseType)
{
    if (setAsset == null) setAsset = t.GetMethod("SetAsset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly);
    if (rebuild == null) rebuild = t.GetMethod("Rebuild", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly);
}

string[] fixtures = { "Assets/Shaper/AuditA24Pyre.asset", "Assets/Shaper/AuditA24Pyre2.asset" };
foreach (var p in fixtures)
{
    var o = UnityEditor.AssetDatabase.LoadMainAssetAtPath(p);
    if (o == null) { sb.Append("MISSING ").Append(p).Append("\n"); continue; }
    UnityEditor.EditorUtility.ClearDirty(o);
    var f = o.GetType().GetField("previewLayerSel");
    var layers = o.GetType().GetField("layers").GetValue(o) as System.Collections.ICollection;
    sb.Append("--- ").Append(p).Append(" layers=").Append(layers.Count)
      .Append(" before: sel=").Append(f.GetValue(o)).Append(" dirty=").Append(UnityEditor.EditorUtility.IsDirty(o)).Append("\n");
    setAsset.Invoke(win, new object[] { o });
    sb.Append("    after bind:    sel=").Append(f.GetValue(o)).Append(" dirty=").Append(UnityEditor.EditorUtility.IsDirty(o)).Append("\n");
    rebuild.Invoke(win, null);
    sb.Append("    after rebuild: sel=").Append(f.GetValue(o)).Append(" dirty=").Append(UnityEditor.EditorUtility.IsDirty(o)).Append("\n");
}

// and the REAL user asset, the one T-0288 and T-0293 both found rewritten
var real = UnityEditor.AssetDatabase.LoadMainAssetAtPath("Assets/Pyre/New Pyre Plus.asset");
UnityEditor.EditorUtility.ClearDirty(real);
var rf = real.GetType().GetField("previewLayerSel");
sb.Append("--- REAL New Pyre Plus.asset before: sel=").Append(rf.GetValue(real)).Append(" dirty=").Append(UnityEditor.EditorUtility.IsDirty(real)).Append("\n");
setAsset.Invoke(win, new object[] { real });
sb.Append("    after bind:    sel=").Append(rf.GetValue(real)).Append(" dirty=").Append(UnityEditor.EditorUtility.IsDirty(real)).Append("\n");
rebuild.Invoke(win, null);
sb.Append("    after rebuild: sel=").Append(rf.GetValue(real)).Append(" dirty=").Append(UnityEditor.EditorUtility.IsDirty(real)).Append("\n");

// does SelLayer still return a valid layer with the sentinel stored?
var selLayerP = pyreT.GetProperty("SelLayer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
sb.Append("SelLayer non-null=").Append(selLayerP == null ? "NOPROP" : (selLayerP.GetValue(win) != null).ToString()).Append("\n");
return sb.ToString();
