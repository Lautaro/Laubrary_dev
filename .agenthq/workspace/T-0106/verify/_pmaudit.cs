var t = System.Type.GetType("Laubrary.Shaper.Editor.ShaperFillAudit");
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var x = a.GetType("Laubrary.Shaper.Editor.ShaperFillAudit"); if (x != null) { t = x; break; } }
if (t == null) return "AUDIT TYPE MISSING";
var sb = new System.Text.StringBuilder();
int pass = 0, fail = 0, ran = 0;
var ms = new System.Collections.Generic.List<System.Reflection.MethodInfo>();
foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static))
  if (m.Name.StartsWith("FT") && m.GetParameters().Length == 0 && m.ReturnType == typeof(string)) ms.Add(m);
ms.Sort((a,b) => string.CompareOrdinal(a.Name, b.Name));
foreach (var m in ms) {
  if (m.Name.StartsWith("FT20")) { sb.Append(m.Name).Append(" SKIPPED (renderer)\n"); continue; }
  string r;
  try { r = (string)m.Invoke(null, null); } catch (System.Exception e) { r = "EXCEPTION " + e.GetBaseException().Message; }
  ran++;
  int p = 0, f = 0, idx = 0;
  while ((idx = r.IndexOf("RESULT: PASS", idx)) >= 0) { p++; idx += 12; }
  idx = 0; while ((idx = r.IndexOf("FAIL", idx)) >= 0) { f++; idx += 4; }
  pass += p; fail += f;
  sb.Append(m.Name).Append("  pass=").Append(p).Append(" failTokens=").Append(f).Append("\n");
}
sb.Append("TOTAL ran=").Append(ran).Append(" RESULT:PASS=").Append(pass).Append(" FAIL tokens=").Append(fail).Append("\n");
return sb.ToString();
