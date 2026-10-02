// Leave exactly one Laubrary tool window open (T334.win) — every other floating tool window is closed,
// because they all sit at the same screen rect and the by-eye channel reads whatever is on TOP.
string keep = UnityEditor.EditorPrefs.GetString("T334.win", "");
string[] unityOwn = { "MainToolbarWindow", "InspectorWindow", "ConsoleWindow", "ProjectBrowser",
                      "SceneHierarchyWindow", "SceneView", "GameView", "PopupWindow" };
var closed = new System.Collections.Generic.List<string>();
foreach (var w in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{
    string n = w.GetType().Name;
    if (n == keep) continue;
    if (System.Array.IndexOf(unityOwn, n) >= 0) continue;
    closed.Add(n); w.Close();
}
var k = ZWin(keep);
if (k != null) { k.Focus(); k.Repaint(); }
UnityEditor.EditorPrefs.SetString("T320.capWin", keep);
return "kept " + keep + " at " + (k != null ? k.position.ToString() : "?") + "; closed " + string.Join(", ", closed);
