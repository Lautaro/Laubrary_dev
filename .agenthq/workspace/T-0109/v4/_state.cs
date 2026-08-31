var sb = new System.Text.StringBuilder();
sb.AppendLine("dataPath=" + UnityEngine.Application.dataPath);
sb.AppendLine("compileFailed=" + UnityEditor.EditorUtility.scriptCompilationFailed);
sb.AppendLine("isCompiling=" + UnityEditor.EditorApplication.isCompiling);
sb.AppendLine("isPlaying=" + UnityEditor.EditorApplication.isPlaying);
sb.AppendLine("SurfaceResolution=" + Laubrary.Shaper.ShaperResolve.SurfaceResolution);
sb.AppendLine("StraightDownTolerance=" + Laubrary.Shaper.ShaperResolve.StraightDownTolerance);
return sb.ToString();
