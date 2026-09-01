string dir = "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0108/verify2/";
string outPath = dir + "regout.txt";
var done = new System.Collections.Generic.HashSet<string>();
if (System.IO.File.Exists(outPath))
  foreach (var l in System.IO.File.ReadAllLines(outPath)) { int sp=l.IndexOf("@@"); if (sp>0) done.Add(l.Substring(0,sp)); }
string[] classes = { "Laubrary.Shaper.Editor.ShaperFieldAudit",
                     "Laubrary.Shaper.Editor.ShaperFillAudit",
                     "Laubrary.Shaper.Editor.ShaperBorderAudit" };
string[] prefixes = { "V", "FT", "BT" };
var sw = System.Diagnostics.Stopwatch.StartNew(); int ran=0, total=0;
for (int c=0;c<classes.Length;c++){
  System.Type t=null;
  foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()){ var x=a.GetType(classes[c]); if(x!=null){t=x;break;} }
  if (t==null){ System.IO.File.AppendAllText(outPath, classes[c]+"|MISSING\n"); continue; }
  var ms = new System.Collections.Generic.List<System.Reflection.MethodInfo>();
  foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static))
    if (m.Name.StartsWith(prefixes[c]) && m.GetParameters().Length==0 && m.ReturnType==typeof(string)) ms.Add(m);
  ms.Sort((a,b)=>string.CompareOrdinal(a.Name,b.Name));
  total += ms.Count;
  foreach (var m in ms){
    string key = prefixes[c]+"|"+m.Name;
    if (done.Contains(key)) continue;
    if (sw.ElapsedMilliseconds > 600000) return "partial ran="+ran+" keep calling";
    string r;
    try { r=(string)m.Invoke(null,null); }
    catch (System.Exception e){ r="  RESULT: FAIL EXCEPTION "+e.GetBaseException().Message; }
    int p=0,idx=0; while((idx=r.IndexOf("RESULT: PASS",idx))>=0){p++;idx+=12;}
    int fr=0; idx=0; while((idx=r.IndexOf("RESULT: FAIL",idx))>=0){fr++;idx+=12;}
    int ft=0; idx=0; while((idx=r.IndexOf("FAIL",idx))>=0){ft++;idx+=4;}
    System.IO.File.AppendAllText(outPath, key+"@@PASS="+p+"|RESULTFAIL="+fr+"|FAILTOK="+ft+"\n");
    ran++;
  }
}
return "complete ran="+ran+" discovered="+total;
