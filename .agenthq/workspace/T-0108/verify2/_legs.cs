string dir = "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0108/verify2/";
string legsFile = dir + "legs.txt", outPath = dir + "legout.txt";
var done = new System.Collections.Generic.HashSet<string>();
if (System.IO.File.Exists(outPath))
  foreach (var l in System.IO.File.ReadAllLines(outPath)) { int sp=l.IndexOf('|'); if (sp>0) done.Add(l.Substring(0,sp)); }
System.Type t = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var x=a.GetType("Laubrary.Shaper.Editor.ShaperLightAudit"); if (x!=null){t=x;break;} }
if (t == null) { System.IO.File.AppendAllText(outPath, "AUDIT TYPE MISSING\n"); return "missing"; }
var sw = System.Diagnostics.Stopwatch.StartNew();
int ran=0;
foreach (var name in System.IO.File.ReadAllLines(legsFile)) {
  string n = name.Trim(); if (n.Length==0 || done.Contains(n)) continue;
  if (sw.ElapsedMilliseconds > 17000) return "partial ran=" + ran + " keep calling";
  var m = t.GetMethod(n, System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static);
  if (m == null) { System.IO.File.AppendAllText(outPath, n + "|NOMETHOD\n"); continue; }
  string r;
  try { r = (string)m.Invoke(null, null); }
  catch (System.Exception e) { r = "RESULT: FAIL EXCEPTION " + e.GetBaseException().Message; }
  int p=0,idx=0; while((idx=r.IndexOf("RESULT: PASS",idx))>=0){p++;idx+=12;}
  int f=0; idx=0; while((idx=r.IndexOf("FAIL",idx))>=0){f++;idx+=4;}
  System.IO.File.AppendAllText(outPath, n + "|PASS=" + p + "|FAILTOK=" + f + "\n");
  System.IO.File.AppendAllText(dir+"legfull.txt", "\n########## " + n + "\n" + r + "\n");
  ran++;
}
return "complete ran=" + ran;
