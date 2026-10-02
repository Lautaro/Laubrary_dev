// Put back the exact window set this session found open (round 15 left these; they are not mine).
var sb = new System.Text.StringBuilder();
string[] want = { "MirageWindow", "ZoeWindow", "TextSplashWindow", "SpriteFxStackWindow",
                  "LarderWindow", "LatheWindow", "PyreWindow", "ShaperWindow" };
foreach (var n in want) { var w = ZOpen(n); sb.Append(n).Append("=").Append(w != null ? "open" : "FAILED").Append("\n"); }
// close anything else this task opened
string[] keepAlso = { "MainToolbarWindow","InspectorWindow","ConsoleWindow","ProjectBrowser",
                      "SceneHierarchyWindow","SceneView","GameView","PopupWindow" };
foreach (var x in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{
    string n = x.GetType().Name;
    if (System.Array.IndexOf(want, n) >= 0 || System.Array.IndexOf(keepAlso, n) >= 0) continue;
    sb.Append("closed ").Append(n).Append("\n"); x.Close();
}
sb.Append("open now: ");
foreach (var x in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) sb.Append(x.GetType().Name).Append(" ");
sb.Append("\n");
// LAST, after every probe that dumps: put the shared output-path pref back where round 15 left it.
UnityEditor.EditorPrefs.SetString("T0312.out", "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0324/out");
sb.Append("T0312.out = ").Append(UnityEditor.EditorPrefs.GetString("T0312.out")).Append("\n");
return sb.ToString();
