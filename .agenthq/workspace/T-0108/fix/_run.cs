string outPath = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0108\fix\LIGHT-AUDIT.txt";
string sheet   = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0108\light-contact-sheet.png";
System.Type t = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var x = a.GetType("Laubrary.Shaper.Editor.ShaperLightAudit"); if (x != null) { t = x; break; } }
if (t == null) return "AUDIT TYPE MISSING";
var done = new System.Collections.Generic.HashSet<string>();
if (System.IO.File.Exists(outPath))
  foreach (var l in System.IO.File.ReadAllLines(outPath))
    if (l.StartsWith("@@LEG ")) done.Add(l.Substring(6).Trim());
var legs = new System.Collections.Generic.List<System.Reflection.MethodInfo>();
foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
  if (m.Name.StartsWith("LT") && m.GetParameters().Length == 0 && m.ReturnType == typeof(string)) legs.Add(m);
legs.Sort((a,b)=>{
  System.Func<string,int> num = s => { int i=2,v=0; while(i<s.Length && char.IsDigit(s[i])) { v=v*10+(s[i]-'0'); i++; } return v; };
  int d = num(a.Name).CompareTo(num(b.Name)); return d != 0 ? d : string.CompareOrdinal(a.Name, b.Name); });
var sw = System.Diagnostics.Stopwatch.StartNew(); int ran = 0;
foreach (var m in legs) {
  if (done.Contains(m.Name)) continue;
  if (sw.ElapsedMilliseconds > 540000) return "partial ran=" + ran + " remaining - call again";
  string r;
  try { r = (string)m.Invoke(null, null); }
  catch (System.Exception e) { r = m.Name + "\n  RESULT: FAIL EXCEPTION " + e.GetBaseException().ToString(); }
  System.IO.File.AppendAllText(outPath, "@@LEG " + m.Name + "\n" + r + "\n\n");
  ran++;
}
if (!done.Contains("LT17_ContactSheet")) {
  string r;
  try { r = (string)t.GetMethod("LT17_ContactSheet").Invoke(null, new object[]{ sheet }); }
  catch (System.Exception e) { r = "LT17\n  RESULT: FAIL EXCEPTION " + e.GetBaseException().ToString(); }
  System.IO.File.AppendAllText(outPath, "@@LEG LT17_ContactSheet\n" + r + "\n\n");
  ran++;
}
return "complete ran=" + ran + " discovered=" + legs.Count;
