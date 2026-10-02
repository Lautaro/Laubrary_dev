System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");

// Shaper: record what it is bound to, then leave it alone
var shaperT = FT("ShaperWindow");
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
    if (w0 != null && w0.GetType() == shaperT)
    {
        sb.Append("SHAPER pos=").Append(w0.position.ToString()).Append("\n");
        foreach (var f in shaperT.GetFields(BFi))
            if (typeof(UnityEngine.ScriptableObject).IsAssignableFrom(f.FieldType))
            { var v = f.GetValue(w0) as UnityEngine.Object; sb.Append("SHAPER field ").Append(f.Name).Append(" = ").Append(v == null ? "null" : UnityEditor.AssetDatabase.GetAssetPath(v)).Append("\n"); }
    }

var pyreT = FT("PyreWindow");
UnityEditor.EditorWindow win = null;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w0 != null && w0.GetType() == pyreT) win = w0;
if (win == null)
{
    var open = pyreT.GetMethod("Open", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
    open.Invoke(null, null);
    foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w0 != null && w0.GetType() == pyreT) win = w0;
    sb.Append("opened Pyre\n");
}
win.position = new UnityEngine.Rect(80f, 60f, 1600f, 900f);
var lpwF = pyreT.GetField("leftPaneWidth", BFi);
sb.Append("intent before=").Append(lpwF.GetValue(win)).Append("\n");
lpwF.SetValue(win, 560f);
System.Reflection.MethodInfo rebuild = null;
for (var t = pyreT; t != null && rebuild == null; t = t.BaseType) rebuild = t.GetMethod("Rebuild", BFi | System.Reflection.BindingFlags.DeclaredOnly);
rebuild.Invoke(win, null); win.Repaint();
sb.Append("rebuilt at ").Append(win.position.width).Append("x").Append(win.position.height).Append(" intent=").Append(lpwF.GetValue(win)).Append("\n");
var lt = System.Type.GetType("UnityEditor.LogEntries,UnityEditor");
lt.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).Invoke(null, null);
sb.Append("console cleared\n");
return sb.ToString();
