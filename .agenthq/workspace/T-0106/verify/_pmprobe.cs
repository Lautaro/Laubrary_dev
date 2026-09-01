var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
sb.Append("compiling=").Append(UnityEditor.EditorApplication.isCompiling)
  .Append(" failed=").Append(UnityEditor.EditorUtility.scriptCompilationFailed).Append("\n");
string[] want = { "Laubrary.Shaper.ShaperFillResolver", "Laubrary.Shaper.ShaperFillCompiler",
                  "Laubrary.Shaper.ShaperFillOps", "Laubrary.Shaper.ShaperFillDef",
                  "Laubrary.Shaper.Editor.ShaperFillAudit", "Laubrary.Shaper.ShaperCompiler" };
foreach (var w in want) {
  System.Type found = null;
  foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var t = a.GetType(w); if (t != null) { found = t; break; } }
  sb.Append(w).Append(" = ").Append(found != null ? "OK" : "MISSING").Append("\n");
}
var audit = System.Type.GetType("Laubrary.Shaper.Editor.ShaperFillAudit, com.Lautaro-Arino.Laubrary.Shaper.Editor");
if (audit == null) foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var t = a.GetType("Laubrary.Shaper.Editor.ShaperFillAudit"); if (t != null) { audit = t; break; } }
int ft = 0; if (audit != null) foreach (var m in audit.GetMethods(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static)) if (m.Name.StartsWith("FT")) ft++;
sb.Append("FT methods=").Append(ft).Append("\n");
var comp = System.Type.GetType("Laubrary.Shaper.ShaperCompiler, com.Lautaro-Arino.Laubrary.Shaper");
if (comp == null) foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var t = a.GetType("Laubrary.Shaper.ShaperCompiler"); if (t != null) { comp = t; break; } }
int ov = 0; if (comp != null) foreach (var m in comp.GetMethods()) if (m.Name == "Compile") ov++;
sb.Append("Compile overloads=").Append(ov).Append("\n");
var res = System.Type.GetType("Laubrary.Shaper.ShaperFillResolver");
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var t = a.GetType("Laubrary.Shaper.ShaperFillResolver"); if (t != null) { res = t; break; } }
bool premul = false; if (res != null) foreach (var m in res.GetMethods()) if (m.Name.Contains("Premultiplied")) premul = true;
sb.Append("EncodePremultiplied still present=").Append(premul).Append(" (expect False)\n");
return sb.ToString();
