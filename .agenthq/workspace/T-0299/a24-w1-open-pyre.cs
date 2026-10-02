var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");

System.Func<string, System.Type> FindType = n => {
    foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
        foreach (var t in a.GetTypes()) if (t.Name == n) return t;
    return null;
};
var pyreT = FindType("PyreWindow");

// find the [MenuItem] that opens it, and invoke it exactly as a user would
System.Reflection.MethodInfo opener = null;
foreach (var m in pyreT.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
{
    var att = m.GetCustomAttributes(typeof(UnityEditor.MenuItem), false);
    if (att.Length > 0) { opener = m; sb.Append("menu=").Append(((UnityEditor.MenuItem)att[0]).menuItem).Append(" method=").Append(m.Name).Append(" params=").Append(m.GetParameters().Length).Append("\n"); }
}
if (opener != null && opener.GetParameters().Length == 0) { opener.Invoke(null, null); sb.Append("invoked menu opener\n"); }
else { UnityEditor.EditorWindow.GetWindow(pyreT); sb.Append("GetWindow fallback\n"); }

var win = UnityEditor.EditorWindow.GetWindow(pyreT);
sb.Append("win=").Append(win != null).Append("\n");

string[] paths = { "Assets/Pyre/New Pyre Plus.asset", "Assets/Pyre/New Pyre Plus 1.asset", "Assets/Pyre/Green Lantern.asset", "Assets/Pyre/New Pyre Plus§.asset" };
foreach (var p in paths)
{
    var o = UnityEditor.AssetDatabase.LoadMainAssetAtPath(p);
    sb.Append("AFTEROPEN ").Append(p).Append(" dirty=").Append(o == null ? false : UnityEditor.EditorUtility.IsDirty(o)).Append("\n");
}
// which asset is the window bound to?
foreach (var f in pyreT.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
{
    if (typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType))
    {
        var v = f.GetValue(win) as UnityEngine.Object;
        if (v != null && f.FieldType.Name == "Pyre") sb.Append("bound field ").Append(f.Name).Append(" = ").Append(UnityEditor.AssetDatabase.GetAssetPath(v)).Append("\n");
    }
}
// every dirty ScriptableObject in the project right now
int nd = 0;
foreach (var o in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.ScriptableObject>())
{
    if (o == null) continue;
    if (!UnityEditor.EditorUtility.IsDirty(o)) continue;
    var ap = UnityEditor.AssetDatabase.GetAssetPath(o);
    if (string.IsNullOrEmpty(ap)) continue;
    if (ap.StartsWith("Library") || ap.StartsWith("Packages/com.unity")) continue;
    nd++; sb.Append("DIRTY-SO ").Append(o.GetType().Name).Append(" @ ").Append(ap).Append(" name=").Append(o.name).Append("\n");
}
sb.Append("dirtyCount=").Append(nd).Append("\n");
return sb.ToString();
