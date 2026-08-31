System.Type t = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var x = a.GetType("Laubrary.Shaper.Editor.ShaperFieldAudit"); if (x != null) { t = x; break; } }
if (t == null) return "TYPE MISSING";
var ms = new System.Collections.Generic.List<System.Reflection.MethodInfo>();
foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static))
  if (m.Name.StartsWith("V") && m.Name.Contains("_") && m.GetParameters().Length == 0 && m.ReturnType == typeof(string)) ms.Add(m);
ms.Sort((a,b) => { int ia = int.Parse(a.Name.Substring(1, a.Name.IndexOf('_')-1)); int ib = int.Parse(b.Name.Substring(1, b.Name.IndexOf('_')-1)); return ia.CompareTo(ib); });
string outPath = @"D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0107/verify/FIELD-AUDIT.txt";
string progPath = @"D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0107/verify/field_progress.txt";
var done = new System.Collections.Generic.HashSet<string>();
if (System.IO.File.Exists(progPath)) foreach (var line in System.IO.File.ReadAllLines(progPath)) if (line.Trim().Length > 0) done.Add(line.Trim());
if (done.Count == 0) System.IO.File.WriteAllText(outPath, "=== FIELD audit re-run under T-0107 (regression check) ===\n");
var sw = System.Diagnostics.Stopwatch.StartNew();
int ran = 0;
foreach (var m in ms) {
  if (done.Contains(m.Name)) continue;
  if (ran > 0 && sw.ElapsedMilliseconds > 15000) break;
  string r; try { r = (string)m.Invoke(null, null); } catch (System.Exception e) { r = m.Name + "\n  RESULT: FAIL EXCEPTION " + e.GetBaseException().ToString(); }
  System.IO.File.AppendAllText(outPath, r + "\n");
  System.IO.File.AppendAllText(progPath, m.Name + "\n");
  ran++;
}
return "ran " + ran + "; total " + ms.Count + "; done " + (done.Count + ran);
