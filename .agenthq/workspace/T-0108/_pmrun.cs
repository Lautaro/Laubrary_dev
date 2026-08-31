string outPath = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0108\PM-VERIFY.txt";
string stPath  = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0108\PM-VERIFY.state";
string[] typeNames = new string[] {
  "Laubrary.Shaper.Editor.ShaperLightAudit","Laubrary.Shaper.Editor.ShaperFieldAudit",
  "Laubrary.Shaper.Editor.ShaperFillAudit","Laubrary.Shaper.Editor.ShaperBorderAudit" };
var legs = new System.Collections.Generic.List<System.Reflection.MethodInfo>();
foreach (var tn in typeNames) {
  System.Type t = null;
  foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { t = a.GetType(tn); if (t != null) break; }
  if (t == null) continue;
  var ms = t.GetMethods(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.DeclaredOnly);
  var loc = new System.Collections.Generic.List<System.Reflection.MethodInfo>();
  foreach (var m in ms) {
    if (m.GetParameters().Length != 0) continue;
    if (m.ReturnType != typeof(string)) continue;
    string n = m.Name;
    if (!(n.StartsWith("LT")||n.StartsWith("V")||n.StartsWith("FT")||n.StartsWith("BT"))) continue;
    loc.Add(m);
  }
  loc.Sort(delegate(System.Reflection.MethodInfo x, System.Reflection.MethodInfo y){ return string.CompareOrdinal(x.Name,y.Name); });
  legs.AddRange(loc);
}
int start = 0;
if (System.IO.File.Exists(stPath)) int.TryParse(System.IO.File.ReadAllText(stPath).Trim(), out start);
if (start == 0) System.IO.File.WriteAllText(outPath, "PM VERIFY RUN — total legs discovered: " + legs.Count + "\r\n");
var sw = System.Diagnostics.Stopwatch.StartNew();
int i = start;
for (; i < legs.Count; i++) {
  string res;
  try { res = (string)legs[i].Invoke(null, null); }
  catch (System.Exception e) { res = "EXCEPTION " + (e.InnerException != null ? e.InnerException.ToString() : e.ToString()); }
  System.IO.File.AppendAllText(outPath, "\r\n===== [" + i + "] " + legs[i].DeclaringType.Name + "." + legs[i].Name + " =====\r\n" + res + "\r\n");
  if (sw.ElapsedMilliseconds > 10000) { i++; break; }
}
System.IO.File.WriteAllText(stPath, i.ToString());
return "ran " + start + ".." + (i-1) + " of " + legs.Count + (i >= legs.Count ? "  ALL DONE" : "  MORE");
