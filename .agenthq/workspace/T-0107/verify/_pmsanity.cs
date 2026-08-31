var sb = new System.Text.StringBuilder();
sb.Append("dataPath=" + UnityEngine.Application.dataPath);
sb.Append("; compiling=" + UnityEditor.EditorApplication.isCompiling);
sb.Append("; failed=" + UnityEditor.EditorUtility.scriptCompilationFailed);
string[] want = new string[] {
  "Laubrary.Shaper.ShaperBorderDef", "Laubrary.Shaper.ShaperBorder",
  "Laubrary.Shaper.ShaperNode", "Laubrary.Shaper.ShaperFillResolver",
  "Laubrary.Shaper.Editor.ShaperBorderAudit", "Laubrary.Shaper.Editor.ShaperFillAudit",
  "Laubrary.Shaper.Editor.ShaperFieldAudit" };
foreach (var w in want) {
  System.Type t = null;
  foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var x = a.GetType(w); if (x != null) { t = x; break; } }
  sb.Append("; " + w.Substring(w.LastIndexOf('.') + 1) + "=" + (t != null));
}
System.Type nodeT = null, opT = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) {
  if (nodeT == null) nodeT = a.GetType("Laubrary.Shaper.ShaperNode");
  if (opT == null) opT = a.GetType("Laubrary.Shaper.ShaperOpKind");
}
sb.Append("; ShaperNode.border=" + (nodeT != null && nodeT.GetField("border") != null));
sb.Append("; OpKind.Dilate=" + (opT != null && System.Enum.IsDefined(opT, "Dilate")));
sb.Append("; supportSpread=" + (System.Array.Exists(System.Array.ConvertAll(
   (nodeT != null ? System.Array.Empty<string>() : System.Array.Empty<string>()), s => s), s => true) ? "n/a" : "n/a"));
System.Type progT = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var x = a.GetType("Laubrary.Shaper.ShaperProgram"); if (x != null) { progT = x; break; } }
sb.Append("; ShaperProgram.supportSpread=" + (progT != null && progT.GetField("supportSpread") != null));
return sb.ToString();
