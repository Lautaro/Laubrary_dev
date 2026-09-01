var sb = new System.Text.StringBuilder();
sb.Append("compiling=").Append(UnityEditor.EditorApplication.isCompiling)
  .Append(" failed=").Append(UnityEditor.EditorUtility.scriptCompilationFailed).Append(" ");
System.Type t = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var x = a.GetType("Laubrary.Shaper.Editor.ShaperLightAudit"); if (x != null) { t = x; break; } }
if (t != null) {
  string[] want = { "LT18_DegenerateDialsNeverGoNonFinite", "LT19_EveryDialIsLiveOrDeclaredInert", "LT20_BorderIsLitByItsHost", "LT21_DocumentOwnsTheLights", "LT22_GlowPathExecutes", "LT23_RimIsInertOnBlackAmbientAndDeclared", "WriteGoldens" };
  foreach (var w in want) sb.Append(w.Substring(0,4)).Append("=").Append(t.GetMethod(w) != null).Append(" ");
} else sb.Append("AUDIT TYPE MISSING ");
return sb.ToString();
