var sb = new System.Text.StringBuilder();
string[] mine = { "T334.menus","T334.wins","T334.tag","T334.walkWin","T334.newName","T334.secWin","T334.find",
                  "T334.narrowWins","T334.narrowW","T334.t0","T334.win","T334.line","T334.scrollY","T334.sprite",
                  "T334.deskOut","T334.newName","T334.findNth","T334.rigOut" };
foreach (var k in mine) if (UnityEditor.EditorPrefs.HasKey(k)) { UnityEditor.EditorPrefs.DeleteKey(k); sb.Append("deleted pref ").Append(k).Append("\n"); }
UnityEditor.EditorPrefs.SetString("T320.capOut", "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0324/shots/cap.png");
UnityEditor.EditorPrefs.SetString("T0312.out", "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0324/out");
sb.Append("restored T320.capOut and T0312.out to their T-0324 values\n");
UnityEditor.Undo.ClearAll();
var cw = System.Type.GetType("UnityEditor.LogEntries,UnityEditor");
if (cw != null) { var m = cw.GetMethod("Clear"); if (m != null) m.Invoke(null, null); }
sb.Append("undo cleared, console cleared\n");
sb.Append("compileFailed=").Append(UnityEditor.EditorUtility.scriptCompilationFailed)
  .Append(" isPlaying=").Append(UnityEditor.EditorApplication.isPlaying).Append("\n");
var s = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
sb.Append("scene=").Append(s.path).Append(" dirty=").Append(s.isDirty).Append("\n");
sb.Append("open windows: ");
foreach (var w in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) sb.Append(w.GetType().Name).Append(" ");
sb.Append("\nminSize check: ");
foreach (var w in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
  if (w.GetType().Name.EndsWith("Window") && w.GetType().Namespace != null && w.GetType().Namespace.StartsWith("Laubrary"))
    sb.Append(w.GetType().Name).Append("=").Append(w.minSize.x).Append(" ");
return sb.ToString();
