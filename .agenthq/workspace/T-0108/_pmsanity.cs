var sb = new System.Text.StringBuilder();
sb.AppendLine("dataPath=" + UnityEngine.Application.dataPath);
sb.AppendLine("isCompiling=" + UnityEditor.EditorApplication.isCompiling);
sb.AppendLine("scriptCompilationFailed=" + UnityEditor.EditorUtility.scriptCompilationFailed);
string[] want = new string[] {
  "Laubrary.Shaper.ShaperLightRig","Laubrary.Shaper.ShaperLightLaw","Laubrary.Shaper.ShaperNormals",
  "Laubrary.Shaper.ShaperLightCompiler","Laubrary.Shaper.ShaperSolids","Laubrary.Shaper.ShaperDocument",
  "Laubrary.Shaper.ShaperLayer","Laubrary.Shaper.ShaperLightAudit","Laubrary.Shaper.ShaperFieldAudit",
  "Laubrary.Shaper.ShaperFillAudit","Laubrary.Shaper.ShaperBorderAudit" };
foreach (var w in want) {
  System.Type t = null;
  foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { t = a.GetType(w); if (t != null) break; }
  sb.AppendLine("TYPE " + w + " = " + (t == null ? "MISSING" : t.Assembly.GetName().Name));
}
System.Type la = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { la = a.GetType("Laubrary.Shaper.ShaperLightAudit"); if (la != null) break; }
if (la != null) {
  var ms = la.GetMethods(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.DeclaredOnly);
  var names = new System.Collections.Generic.List<string>();
  foreach (var m in ms) if (m.Name.StartsWith("LT")) names.Add(m.Name);
  names.Sort();
  sb.AppendLine("LT methods (" + names.Count + "): " + string.Join(",", names.ToArray()));
}
System.Type law = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { law = a.GetType("Laubrary.Shaper.ShaperLightLaw"); if (law != null) break; }
if (law != null) {
  var shades = law.GetMethods(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static);
  int n = 0; string sig = "";
  foreach (var m in shades) if (m.Name == "Shade") { n++; int arr=0, refs=0;
     foreach (var p in m.GetParameters()) { var pt = p.ParameterType; var et = pt.IsByRef ? pt.GetElementType() : pt;
        if (et.IsArray) arr++; if (!et.IsValueType) refs++; }
     sig = "params=" + m.GetParameters().Length + " arrayParams=" + arr + " refTypeParams=" + refs; }
  sb.AppendLine("LR-2.2/LT-13b: public Shade methods=" + n + " " + sig);
}
return sb.ToString();
