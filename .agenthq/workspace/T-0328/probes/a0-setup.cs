// T-0328 session setup: record the prefs I am about to change, point the dump dir at my own folder.
var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
sb.Append("playing=").Append(UnityEditor.EditorApplication.isPlaying)
  .Append(" compileFailed=").Append(UnityEditor.EditorUtility.scriptCompilationFailed).Append("\n");
sb.Append("PREV T0312.out=").Append(UnityEditor.EditorPrefs.GetString("T0312.out", "<unset>")).Append("\n");
sb.Append("PREV T320.capWin=").Append(UnityEditor.EditorPrefs.GetString("T320.capWin", "<unset>")).Append("\n");
sb.Append("PREV T320.capOut=").Append(UnityEditor.EditorPrefs.GetString("T320.capOut", "<unset>")).Append("\n");
sb.Append("PREV userSel=").Append(UnityEditor.EditorPrefs.GetString("ZuiSectionToggleBar.ShaperWindow.userSel", "<unset>")).Append("\n");
UnityEditor.EditorPrefs.SetString("T0312.out", "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0328/out");
UnityEditor.EditorPrefs.SetString("T320.capOut", "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0328/shots/cap.png");
sb.Append("scene=").Append(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path)
  .Append(" dirty=").Append(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isDirty).Append("\n");
sb.Append("open windows: ");
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) sb.Append(w.GetType().Name).Append(" ");
return sb.ToString();
