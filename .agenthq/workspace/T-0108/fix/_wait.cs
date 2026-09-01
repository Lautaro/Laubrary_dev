var sb = new System.Text.StringBuilder();
sb.Append("compiling=").Append(UnityEditor.EditorApplication.isCompiling);
sb.Append(" failed=").Append(UnityEditor.EditorUtility.scriptCompilationFailed);
sb.Append(" dataPath=").Append(UnityEngine.Application.dataPath);
return sb.ToString();
