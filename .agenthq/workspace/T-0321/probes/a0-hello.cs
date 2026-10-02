UnityEditor.EditorPrefs.SetString("T0312.out","D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0321/out");
var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(Application.dataPath).Append("\n");
sb.Append("compileFailed=").Append(UnityEditor.EditorUtility.scriptCompilationFailed).Append("\n");
sb.Append("isPlaying=").Append(UnityEditor.EditorApplication.isPlaying).Append("\n");
sb.Append("pp=").Append(UnityEditor.EditorGUIUtility.pixelsPerPoint).Append("\n");
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
  if (w!=null) sb.Append("win: ").Append(w.GetType().Name).Append(" '").Append(w.titleContent.text).Append("' ").Append(w.position).Append("\n");
return sb.ToString();
