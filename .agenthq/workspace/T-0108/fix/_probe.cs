var sb = new System.Text.StringBuilder();
System.Type td = null, tp = null, ts = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) {
  if (td == null) td = a.GetType("Laubrary.Shaper.ShaperSolidDial");
  if (tp == null) tp = a.GetType("Laubrary.Shaper.ShaperLightProgram");
  if (ts == null) ts = a.GetType("Laubrary.Shaper.ShaperLightRig");
}
sb.Append("ShaperSolidDial=").Append(td != null);
sb.Append(" enabledLightCount=").Append(tp != null && tp.GetField("enabledLightCount") != null);
sb.Append(" hasInertDial=").Append(tp != null && tp.GetField("hasInertDial") != null);
sb.Append(" hasInertRim=").Append(tp != null && tp.GetField("hasInertRim") != null);
sb.Append(" RimNeedsBlackAmbientRelief=").Append(ts != null && ts.GetField("RimNeedsBlackAmbientRelief") != null);
sb.Append(" failed=").Append(UnityEditor.EditorUtility.scriptCompilationFailed);
return sb.ToString();
