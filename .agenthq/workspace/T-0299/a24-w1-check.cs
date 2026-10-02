var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
System.Func<string, System.Type> FindType = n => {
    foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
        foreach (var t in a.GetTypes()) if (t.Name == n) return t;
    return null;
};
var pyreT = FindType("PyreWindow");
UnityEditor.EditorWindow win = null;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
    if (w != null && w.GetType() == pyreT) win = w;
sb.Append("pyreWinOpen=").Append(win != null).Append("\n");

if (win != null)
{
    for (var t = pyreT; t != null && t != typeof(UnityEditor.EditorWindow); t = t.BaseType)
    foreach (var f in t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly))
    {
        if (!typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType)) continue;
        var v = f.GetValue(win) as UnityEngine.Object;
        if (v == null) continue;
        var ap = UnityEditor.AssetDatabase.GetAssetPath(v);
        if (!string.IsNullOrEmpty(ap)) sb.Append("FIELD ").Append(t.Name).Append(".").Append(f.Name).Append(" (").Append(f.FieldType.Name).Append(") -> ").Append(ap).Append(" dirty=").Append(UnityEditor.EditorUtility.IsDirty(v)).Append("\n");
    }
}

int nd = 0;
foreach (var o in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.ScriptableObject>())
{
    if (o == null) continue;
    if (!UnityEditor.EditorUtility.IsDirty(o)) continue;
    var ap = UnityEditor.AssetDatabase.GetAssetPath(o);
    if (string.IsNullOrEmpty(ap)) continue;
    nd++; sb.Append("DIRTY-SO ").Append(o.GetType().Name).Append(" @ ").Append(ap).Append("\n");
}
sb.Append("dirtyCount=").Append(nd).Append("\n");
return sb.ToString();
