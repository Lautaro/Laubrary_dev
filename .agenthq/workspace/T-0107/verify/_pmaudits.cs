string outPath = "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0107/verify/_pmaudits_result.txt";
var done = new System.Collections.Generic.HashSet<string>();
if (System.IO.File.Exists(outPath))
  foreach (var line in System.IO.File.ReadAllLines(outPath)) { int sp = line.IndexOf(' '); if (sp > 0) done.Add(line.Substring(0, sp)); }

string[] classes = new string[] {
  "Laubrary.Shaper.Editor.ShaperBorderAudit",
  "Laubrary.Shaper.Editor.ShaperFillAudit",
  "Laubrary.Shaper.Editor.ShaperFieldAudit" };
string[] prefixes = new string[] { "BT", "FT", "V" };

var sw = System.Diagnostics.Stopwatch.StartNew();
int ran = 0, total = 0;
for (int c = 0; c < classes.Length; c++) {
  System.Type t = null;
  foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var x = a.GetType(classes[c]); if (x != null) { t = x; break; } }
  if (t == null) { System.IO.File.AppendAllText(outPath, classes[c] + " MISSING\n"); continue; }
  var ms = new System.Collections.Generic.List<System.Reflection.MethodInfo>();
  foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
    if (m.Name.StartsWith(prefixes[c]) && m.GetParameters().Length == 0 && m.ReturnType == typeof(string)) ms.Add(m);
  ms.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
  total += ms.Count;
  foreach (var m in ms) {
    string key = prefixes[c] + "|" + m.Name;
    if (done.Contains(key)) continue;
    if (sw.ElapsedMilliseconds > 18000) { return "partial: ran " + ran + " this call; keep calling"; }
    string r;
    try { r = (string)m.Invoke(null, null); }
    catch (System.Exception e) { r = "  RESULT: FAIL EXCEPTION " + e.GetBaseException().Message; }
    int p = 0, idx = 0; while ((idx = r.IndexOf("RESULT: PASS", idx)) >= 0) { p++; idx += 12; }
    int f = 0; idx = 0; while ((idx = r.IndexOf("FAIL", idx)) >= 0) { f++; idx += 4; }
    System.IO.File.AppendAllText(outPath, key + " passResults=" + p + " failTokens=" + f + "\n");
    ran++;
  }
}
return "complete this call: ran " + ran + "; legs discovered " + total;
