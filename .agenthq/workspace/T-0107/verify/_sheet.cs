System.Type t = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var x = a.GetType("Laubrary.Shaper.Editor.ShaperBorderAudit"); if (x != null) { t = x; break; } }
if (t == null) return "TYPE MISSING";
var m = t.GetMethod("BorderContactSheet");
string r = (string)m.Invoke(null, new object[] { @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0107\border-contact-sheet.png" });
System.IO.File.WriteAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0107\verify\contact-sheet-report.txt", r);
return "written, " + r.Length + " chars";
