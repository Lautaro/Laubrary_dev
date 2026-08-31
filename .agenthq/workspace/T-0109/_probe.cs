// T-0109 compile probe. Fully qualified everywhere: the CLI's eval_file wraps this in a method body,
// so `using` aliases at the top silently fail (T-0105's gotcha).
var sb = new System.Text.StringBuilder();
sb.AppendLine("dataPath = " + UnityEngine.Application.dataPath);
sb.AppendLine("scriptCompilationFailed = " + UnityEditor.EditorUtility.scriptCompilationFailed);
var t = System.Type.GetType("Laubrary.Shaper.ShaperHeight, com.Lautaro-Arino.Laubrary.Shaper");
sb.AppendLine("ShaperHeight type = " + (t == null ? "NULL" : t.FullName));
var r = System.Type.GetType("Laubrary.Shaper.ShaperResolve, com.Lautaro-Arino.Laubrary.Shaper");
sb.AppendLine("ShaperResolve type = " + (r == null ? "NULL" : r.FullName));
var a = System.Type.GetType("Laubrary.Shaper.Editor.ShaperHeightAudit, com.Lautaro-Arino.Laubrary.Shaper.Editor");
sb.AppendLine("ShaperHeightAudit type = " + (a == null ? "NULL" : a.FullName));
return sb.ToString();
