var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
System.Func<string, System.Type> FindType = n => {
    foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
        foreach (var t in a.GetTypes()) if (t.Name == n) return t;
    return null;
};
var pyreT = FindType("PyreWindow");
string dup = "Assets/Shaper/AuditA24Pyre.asset";
var o = UnityEditor.AssetDatabase.LoadMainAssetAtPath(dup);
var f = o.GetType().GetField("previewLayerSel");

sb.Append("BEFORE dirty=").Append(UnityEditor.EditorUtility.IsDirty(o)).Append(" previewLayerSel=").Append(f.GetValue(o)).Append("\n");

UnityEditor.EditorWindow win = null;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
    if (w != null && w.GetType() == pyreT) win = w;
sb.Append("winFound=").Append(win != null).Append("\n");

System.Reflection.MethodInfo setAsset = null;
for (var t = pyreT; t != null; t = t.BaseType)
{
    var m = t.GetMethod("SetAsset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly);
    if (m != null) { setAsset = m; sb.Append("SetAsset on ").Append(t.Name).Append("\n"); break; }
}
setAsset.Invoke(win, new object[] { o });

sb.Append("AFTER-SETASSET dirty=").Append(UnityEditor.EditorUtility.IsDirty(o)).Append(" previewLayerSel=").Append(f.GetValue(o)).Append("\n");
win.Repaint();
return sb.ToString();
