var sb = new System.Text.StringBuilder();
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
  if (w != null) sb.Append("open: ").Append(w.GetType().Name).Append("\n");
// every T334.* key this session made
foreach (var k in new string[]{ "T334.focus","T334.walkWin","T334.newName","T334.h","T334.w","T334.tex0","T334.t0","T334.cmpDoc","T334.cmpPng" })
  if (UnityEditor.EditorPrefs.HasKey(k)) { UnityEditor.EditorPrefs.DeleteKey(k); sb.Append("deleted pref ").Append(k).Append("\n"); }
UnityEditor.EditorPrefs.SetString("T320.capOut", "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0324/shots/cap.png");
sb.Append("T320.capOut restored\n");
sb.Append("T320.capWin=").Append(UnityEditor.EditorPrefs.GetString("T320.capWin","<unset>")).Append(" (left as found)\n");
UnityEditor.Undo.ClearAll();
UnityEditor.EditorApplication.ExecuteMenuItem("Edit/Clear Console");
sb.Append("undo cleared, console cleared\n");
return sb.ToString();
