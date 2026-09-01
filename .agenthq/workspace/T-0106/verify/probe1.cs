var sb = new System.Text.StringBuilder();
sb.AppendLine("dataPath=" + UnityEngine.Application.dataPath);
sb.AppendLine("compileFailed=" + UnityEditor.EditorUtility.scriptCompilationFailed);
sb.AppendLine("isCompiling=" + UnityEditor.EditorApplication.isCompiling);
var t = System.Type.GetType("Laubrary.Shaper.Editor.ShaperFillAudit, com.Lautaro-Arino.Laubrary.Shaper.Editor");
sb.AppendLine("auditType=" + (t == null ? "NULL" : t.FullName));
if (t != null) {
  sb.AppendLine("FT22=" + (t.GetMethod("FT22_EncodeIsReadableBack") != null));
  sb.AppendLine("StampSeam=" + (t.GetMethod("StampSeam", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static) != null));
}
var r = System.Type.GetType("Laubrary.Shaper.ShaperFillResolver, com.Lautaro-Arino.Laubrary.Shaper");
if (r != null) {
  sb.AppendLine("Encode=" + (r.GetMethod("Encode") != null));
  sb.AppendLine("EncodePremultiplied gone=" + (r.GetMethod("EncodePremultiplied") == null));
}
var b = System.Type.GetType("Laubrary.Shaper.ShaperFillBuffers, com.Lautaro-Arino.Laubrary.Shaper");
if (b != null) sb.AppendLine("subtree field=" + (b.GetField("subtree") != null));
return sb.ToString();
