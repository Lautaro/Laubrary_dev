var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
System.Func<string, System.Type> FindType = n => {
    foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
        foreach (var t in a.GetTypes()) if (t.Name == n) return t;
    return null;
};
var pyreT = FindType("PyreWindow");

// duplicate of an asset whose previewLayerSel is ALREADY VALID (0) -> the BuildAsset clamp is a no-op,
// so any dirty flag can only have come from OnAssetChanged's layerSel = int.MaxValue stomp.
string src = "Assets/Pyre/New Pyre Plus 1.asset";
string dup = "Assets/Shaper/AuditA24Pyre2.asset";
if (UnityEditor.AssetDatabase.LoadMainAssetAtPath(dup) != null) UnityEditor.AssetDatabase.DeleteAsset(dup);
UnityEditor.AssetDatabase.CopyAsset(src, dup);
UnityEditor.AssetDatabase.Refresh();
var o = UnityEditor.AssetDatabase.LoadMainAssetAtPath(dup);
var f = o.GetType().GetField("previewLayerSel");
var layers = o.GetType().GetField("layers").GetValue(o) as System.Collections.ICollection;
sb.Append("dup2 layers=").Append(layers.Count).Append(" previewLayerSel=").Append(f.GetValue(o)).Append(" dirty=").Append(UnityEditor.EditorUtility.IsDirty(o)).Append("\n");

UnityEditor.EditorWindow win = null;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
    if (w != null && w.GetType() == pyreT) win = w;
System.Reflection.MethodInfo setAsset = null;
for (var t = pyreT; t != null; t = t.BaseType)
{
    var m = t.GetMethod("SetAsset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly);
    if (m != null) { setAsset = m; break; }
}
setAsset.Invoke(win, new object[] { o });
sb.Append("AFTER-BIND dirty=").Append(UnityEditor.EditorUtility.IsDirty(o)).Append(" previewLayerSel=").Append(f.GetValue(o)).Append("\n");

// and now: does merely REBUILDING (not rebinding) dirty it again after we clear?
UnityEditor.EditorUtility.ClearDirty(o);
sb.Append("cleared dirty=").Append(UnityEditor.EditorUtility.IsDirty(o)).Append("\n");
System.Reflection.MethodInfo rebuild = null;
for (var t = pyreT; t != null; t = t.BaseType)
{
    var m = t.GetMethod("Rebuild", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly);
    if (m != null) { rebuild = m; break; }
}
rebuild.Invoke(win, null);
sb.Append("AFTER-REBUILD dirty=").Append(UnityEditor.EditorUtility.IsDirty(o)).Append(" previewLayerSel=").Append(f.GetValue(o)).Append("\n");
return sb.ToString();
