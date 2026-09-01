var sb = new System.Text.StringBuilder();
sb.Append("compiling=").Append(UnityEditor.EditorApplication.isCompiling)
  .Append(" compileFailed=").Append(UnityEditor.EditorUtility.scriptCompilationFailed);
string[] want = { "Laubrary.Shaper.ShaperBorderDef", "Laubrary.Shaper.ShaperBorder",
                  "Laubrary.Shaper.ShaperResolvedBorder", "Laubrary.Shaper.Editor.ShaperBorderAudit" };
foreach (var w in want) {
  System.Type t = null;
  foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var x = a.GetType(w); if (x != null) { t = x; break; } }
  sb.Append(" | ").Append(w.Substring(w.LastIndexOf('.')+1)).Append("=").Append(t != null ? "OK" : "MISSING");
}
System.Type nt = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var x = a.GetType("Laubrary.Shaper.ShaperNode"); if (x != null) { nt = x; break; } }
sb.Append(" | ShaperNode.border=").Append(nt != null && nt.GetField("border") != null ? "OK" : "MISSING");
sb.Append(" | Dilate=").Append(System.Enum.IsDefined(System.Type.GetType("Laubrary.Shaper.ShaperOpKind, " + (nt!=null?nt.Assembly.GetName().Name:"")), "Dilate"));
return sb.ToString();
