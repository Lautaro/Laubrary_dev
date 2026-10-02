var sb = new System.Text.StringBuilder();
sb.AppendLine("dataPath=" + Application.dataPath);
sb.AppendLine("compileFailed=" + UnityEditor.EditorUtility.scriptCompilationFailed);
sb.AppendLine("isPlaying=" + Application.isPlaying);
sb.AppendLine("isCompiling=" + UnityEditor.EditorApplication.isCompiling);
foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>()) sb.AppendLine("win: " + w.GetType().Name + " '" + w.titleContent.text + "' " + w.position);
return sb.ToString();
