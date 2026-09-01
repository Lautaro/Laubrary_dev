System.Type t = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var x = a.GetType("Laubrary.Shaper.Editor.ShaperFieldAudit"); if (x != null) { t = x; break; } }
if (t == null) return "FIELD AUDIT TYPE MISSING";
var ms = new System.Collections.Generic.List<System.Reflection.MethodInfo>();
foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static))
  if (m.Name.StartsWith("V") && m.GetParameters().Length == 0 && m.ReturnType == typeof(string)) ms.Add(m);
ms.Sort((a,b) => string.CompareOrdinal(a.Name, b.Name));
string outPath = "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0106/verify/_pmfield_result.txt";
var done = new System.Collections.Generic.HashSet<string>();
if (System.IO.File.Exists(outPath)) foreach (var line in System.IO.File.ReadAllLines(outPath)) { int sp = line.IndexOf(' '); if (sp > 0) done.Add(line.Substring(0, sp)); }
var sb = new System.Text.StringBuilder();
var sw = System.Diagnostics.Stopwatch.StartNew();
int ran = 0;
foreach (var m in ms) {
  if (done.Contains(m.Name)) continue;
  if (sw.ElapsedMilliseconds > 18000) break;
  string r; try { r = (string)m.Invoke(null, null); } catch (System.Exception e) { r = "FAIL EXCEPTION " + e.GetBaseException().Message; }
  int p = 0, idx = 0; while ((idx = r.IndexOf("RESULT: PASS", idx)) >= 0) { p++; idx += 12; }
  int f = 0; idx = 0; while ((idx = r.IndexOf("FAIL", idx)) >= 0) { f++; idx += 4; }
  System.IO.File.AppendAllText(outPath, m.Name + " pass=" + p + " failTokens=" + f + "\n");
  ran++;
}
return "ran " + ran + " this call; total legs " + ms.Count + "; recorded " + (done.Count + ran);
