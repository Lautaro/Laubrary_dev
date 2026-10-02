// COLD WALK 1, step 1: close every Chunks window, open it from its own menu item, and read the empty state.
var sb = new System.Text.StringBuilder();
foreach (var x in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{
    string n = x.GetType().Name;
    if (n == "MainToolbarWindow" || n == "InspectorWindow" || n == "ConsoleWindow" || n == "ProjectBrowser"
        || n == "SceneHierarchyWindow" || n == "SceneView" || n == "GameView" || n == "PopupWindow") continue;
    x.Close();
}
bool opened = UnityEditor.EditorApplication.ExecuteMenuItem("Laubrary/Chunks");
sb.Append("menu 'Laubrary/Chunks' -> ").Append(opened).Append("\n");
var w = ZWin("ChunkWindow");
if (w == null) return sb.Append("no ChunkWindow after the menu item").ToString();
w.position = new Rect(40, 40, 1000, 900);
w.titleContent = new GUIContent("Chunks");
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo af = null;
for (var t = w.GetType(); t != null; t = t.BaseType) if (af == null) af = t.GetField("asset", BFi|System.Reflection.BindingFlags.DeclaredOnly);
sb.Append("bound=").Append(af == null ? "<no field>" : (af.GetValue(w) == null ? "<null>" : af.GetValue(w).ToString())).Append("\n");
sb.Append("pos=").Append(w.position).Append("\n");
return sb.ToString();
