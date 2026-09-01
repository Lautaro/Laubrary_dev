System.Type t = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var x = a.GetType("Laubrary.Shaper.Editor.ShaperBorderAudit"); if (x != null) { t = x; break; } }
if (t == null) return "AUDIT TYPE MISSING (editor asmdef did not compile)";
var names = new System.Collections.Generic.List<string>();
foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static))
  if (m.Name=="BoxContainment"||m.Name=="HostlessOrdering"||m.Name=="ClosureAllocations"||m.Name=="RingPrimitive"||m.Name=="StripAnchorWidthDiffs") names.Add(m.Name);
return "audit type OK; new members: " + string.Join(",", names.ToArray()) + "; compileFailed=" + UnityEditor.EditorUtility.scriptCompilationFailed;
