// Bind every open ZuiAssetWindow to the first asset of its own type, so the sweep sees a POPULATED window.
var sb = new System.Text.StringBuilder();
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
string[] unityOwn = { "MainToolbarWindow","InspectorWindow","ConsoleWindow","ProjectBrowser","SceneHierarchyWindow","SceneView","GameView","PopupWindow" };
foreach (var win in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{
    string wn = win.GetType().Name;
    if (System.Array.IndexOf(unityOwn, wn) >= 0) continue;
    System.Reflection.MethodInfo sa = null;
    for (var t = win.GetType(); t != null; t = t.BaseType)
        if (sa == null) sa = t.GetMethod("SetAsset", BFi|System.Reflection.BindingFlags.DeclaredOnly);
    if (sa == null) { sb.Append(wn).Append(": no SetAsset\n"); continue; }
    var ty = sa.GetParameters()[0].ParameterType;
    string best = null;
    foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:" + ty.Name))
    {
        var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
        if (p.Contains("/Samples~/")) continue;
        if (UnityEditor.AssetDatabase.LoadAssetAtPath(p, ty) == null) continue;
        best = p;                       // prefer a demo asset with real content
        if (p.Contains("/Demos/")) break;
    }
    if (best == null) { sb.Append(wn).Append(": no asset of ").Append(ty.Name).Append("\n"); continue; }
    sb.Append(ZBind(wn, best)).Append("\n");
}
return sb.ToString();
