// Prefs and the open-window set, back to what this session found.
var sb = new System.Text.StringBuilder();
sb.Append("NOW ShaperWindow.userSel = ").Append(UnityEditor.EditorPrefs.GetString("ZuiSectionToggleBar.ShaperWindow.userSel", "<unset>")).Append("\n");
sb.Append("NOW ZoeWindow.userSel    = ").Append(UnityEditor.EditorPrefs.GetString("ZuiSectionToggleBar.ZoeWindow.userSel", "<unset>")).Append("\n");
sb.Append("NOW T320.capWin = ").Append(UnityEditor.EditorPrefs.GetString("T320.capWin", "<unset>")).Append("\n");
// close every window this session opened; keep the eight that were open at session start
string[] keep = { "PyreWindow","LatheWindow","LarderWindow","SpriteFxStackWindow","TextSplashWindow",
                  "ZoeWindow","MirageWindow","ShaperWindow" };
string[] unityOwn = { "MainToolbarWindow","InspectorWindow","ConsoleWindow","ProjectBrowser",
                      "SceneHierarchyWindow","SceneView","GameView","PopupWindow" };
var closed = new System.Collections.Generic.List<string>();
foreach (var w in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{
    string n = w.GetType().Name;
    if (System.Array.IndexOf(keep, n) >= 0) continue;
    if (System.Array.IndexOf(unityOwn, n) >= 0) continue;
    closed.Add(n); w.Close();
}
sb.Append("closed: ").Append(string.Join(", ", closed.ToArray())).Append("\n");
return sb.ToString();
