var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
sb.Append("pid=").Append(System.Diagnostics.Process.GetCurrentProcess().Id).Append("\n");
sb.Append("isPlaying=").Append(UnityEditor.EditorApplication.isPlaying)
  .Append(" isCompiling=").Append(UnityEditor.EditorApplication.isCompiling)
  .Append(" compileFailed=").Append(UnityEditor.EditorUtility.scriptCompilationFailed).Append("\n");
var s = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
sb.Append("scene=").Append(s.path).Append(" dirty=").Append(s.isDirty).Append("\n");
sb.Append("ppp=").Append(UnityEditor.EditorGUIUtility.pixelsPerPoint).Append("\n");
sb.Append("T0312.out=").Append(UnityEditor.EditorPrefs.GetString("T0312.out","<unset>")).Append("\n");
sb.Append("T320.capOut=").Append(UnityEditor.EditorPrefs.GetString("T320.capOut","<unset>")).Append("\n");
sb.Append("T320.capWin=").Append(UnityEditor.EditorPrefs.GetString("T320.capWin","<unset>")).Append("\n");
sb.Append("openWindows:\n");
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
  if (w != null) sb.Append("  ").Append(w.GetType().Name).Append(" '").Append(w.titleContent.text).Append("' ").Append(w.position).Append("\n");
return sb.ToString();
