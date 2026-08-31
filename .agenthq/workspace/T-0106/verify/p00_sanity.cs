var sb = new System.Text.StringBuilder();
sb.AppendLine("dataPath = " + UnityEngine.Application.dataPath);
sb.AppendLine("compileFailed = " + UnityEditor.EditorUtility.scriptCompilationFailed);
var t = System.Type.GetType("Laubrary.Shaper.Editor.ShaperFillAudit, Assembly-CSharp-Editor");
if (t == null) {
  foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies()) {
    var x = asm.GetType("Laubrary.Shaper.Editor.ShaperFillAudit");
    if (x != null) { t = x; sb.AppendLine("found in " + asm.GetName().Name); break; }
  }
}
sb.AppendLine("ShaperFillAudit type = " + (t == null ? "NULL" : t.FullName));
if (t != null) {
  var ms = t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
  sb.AppendLine("methods = " + ms.Length);
}
var t2 = (System.Type)null;
foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies()) { var x = asm.GetType("Laubrary.Shaper.ShaperFillResolver"); if (x != null) { t2 = x; break; } }
sb.AppendLine("ShaperFillResolver = " + (t2 == null ? "NULL" : t2.Assembly.GetName().Name));
return sb.ToString();
