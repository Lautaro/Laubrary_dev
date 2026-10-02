// COLD WALK: close everything, open TextSplash from its own menu item, read the empty state.
var sb = new System.Text.StringBuilder();
foreach (var x in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{
    string n = x.GetType().Name;
    if (n == "MainToolbarWindow" || n == "InspectorWindow" || n == "ConsoleWindow" || n == "ProjectBrowser"
        || n == "SceneHierarchyWindow" || n == "SceneView" || n == "GameView" || n == "PopupWindow") continue;
    x.Close();
}
string menu = UnityEditor.EditorPrefs.GetString("T334.menu", "Laubrary/TextSplash");
sb.Append("menu '").Append(menu).Append("' -> ").Append(UnityEditor.EditorApplication.ExecuteMenuItem(menu)).Append("\n");
var w = ZWin("TextSplashWindow");
if (w == null) return sb.Append("no TextSplashWindow").ToString();
w.position = new Rect(40, 40, 1000, 900);
w.titleContent = new GUIContent("TextSplash");
UnityEditor.EditorPrefs.SetString("T334.walkWin", "TextSplashWindow");
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo af = null;
for (var t = w.GetType(); t != null; t = t.BaseType) if (af == null) af = t.GetField("asset", BFi|System.Reflection.BindingFlags.DeclaredOnly);
sb.Append("bound=").Append(af == null ? "<no field>" : (af.GetValue(w) == null ? "<null>" : af.GetValue(w).ToString())).Append("\n");
return sb.ToString();
