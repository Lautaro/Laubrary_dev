var sb = new System.Text.StringBuilder();
sb.AppendLine("dataPath=" + UnityEngine.Application.dataPath);
sb.AppendLine("compileFailed=" + UnityEditor.EditorUtility.scriptCompilationFailed);
sb.AppendLine("isCompiling=" + UnityEditor.EditorApplication.isCompiling);
sb.AppendLine("isPlaying=" + UnityEditor.EditorApplication.isPlaying);
System.Type ht = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var x = a.GetType("Laubrary.Shaper.ShaperHeight"); if (x != null) { ht = x; break; } }
sb.AppendLine("ShaperHeight found = " + (ht != null) + (ht != null ? " asm=" + ht.Assembly.GetName().Name : ""));
if (ht != null) {
  var mi = ht.GetMethod("InverseAtEUpperBound", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
  sb.AppendLine("InverseAtEUpperBound = " + (mi == null ? "ABSENT" : mi.ToString()));
  foreach (var n in new string[]{"SteppedProfileInverseExact","SteppedBevelInverseExact"}) {
    var m2 = ht.GetMethod(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    sb.AppendLine(n + " = " + (m2 == null ? "ABSENT" : "PRESENT"));
  }
}
System.Type rs = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var x = a.GetType("Laubrary.Shaper.ShaperResolve"); if (x != null) { rs = x; break; } }
if (rs != null) {
  foreach (var m in rs.GetMethods(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static))
    if (m.Name == "ClipSlab") sb.AppendLine("ClipSlab sig = " + m.ToString());
}
foreach (var n in new string[]{"ShaperFieldAudit","ShaperFillAudit","ShaperBorderAudit","ShaperLightAudit","ShaperHeightAudit"}) {
  System.Type t = null;
  foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var x = a.GetType("Laubrary.Shaper.Editor." + n); if (x != null) { t = x; break; } }
  sb.AppendLine(n + " = " + (t == null ? "NOT FOUND" : "ok asm=" + t.Assembly.GetName().Name));
}
return sb.ToString();
