var sb = new System.Text.StringBuilder();
sb.AppendLine("dataPath=" + UnityEngine.Application.dataPath);
sb.AppendLine("compiling=" + UnityEditor.EditorApplication.isCompiling);
sb.AppendLine("updating=" + UnityEditor.EditorApplication.isUpdating);
sb.AppendLine("scriptCompilationFailed=" + UnityEditor.EditorUtility.scriptCompilationFailed);
var t = System.Type.GetType("Laubrary.Shaper.ShaperHeight, com.Lautaro-Arino.Laubrary.Shaper");
sb.AppendLine("ShaperHeight type=" + (t == null ? "NULL" : t.FullName));
if (t != null) {
  var m = t.GetMethod("InverseAtEUpperBound", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
  sb.AppendLine("NEW SYMBOL InverseAtEUpperBound=" + (m == null ? "MISSING" : "PRESENT " + m.ToString()));
}
return sb.ToString();
