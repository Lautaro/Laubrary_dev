System.Type Find(string n){ foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies()){ var x=a.GetType(n); if(x!=null) return x; } return null; }
var sw = System.Diagnostics.Stopwatch.StartNew();
string r = (string)Find("Laubrary.Shaper.Editor.ShaperFieldAudit").GetMethod("RunAll").Invoke(null, null);
return r + "\n[elapsed " + sw.ElapsedMilliseconds + " ms]";
