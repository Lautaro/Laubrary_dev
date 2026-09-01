string outPath = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0108\fix\MUT3.txt";
string legFile = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0108\fix\legs.txt";
System.Type t = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var x = a.GetType("Laubrary.Shaper.Editor.ShaperLightAudit"); if (x != null) { t = x; break; } }
if (t == null) return "AUDIT TYPE MISSING";
var done = new System.Collections.Generic.HashSet<string>();
if (System.IO.File.Exists(outPath))
  foreach (var l in System.IO.File.ReadAllLines(outPath)) if (l.StartsWith("@@LEG ")) done.Add(l.Substring(6).Trim());
var sw = System.Diagnostics.Stopwatch.StartNew(); int ran = 0;
foreach (var raw in System.IO.File.ReadAllLines(legFile)) {
  string nm = raw.Trim(); if (nm.Length == 0 || done.Contains(nm)) continue;
  if (sw.ElapsedMilliseconds > 540000) return "partial ran=" + ran + " - call again";
  var m = t.GetMethod(nm);
  string r;
  if (m == null) r = nm + "\n  RESULT: FAIL NO SUCH METHOD";
  else { try { r = (string)m.Invoke(null, null); } catch (System.Exception e) { r = nm + "\n  RESULT: FAIL EXCEPTION " + e.GetBaseException().Message; } }
  System.IO.File.AppendAllText(outPath, "@@LEG " + nm + "\n" + r + "\n\n");
  ran++;
}
return "complete ran=" + ran;
