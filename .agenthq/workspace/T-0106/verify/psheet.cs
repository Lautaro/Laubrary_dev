System.Type Find(string n){ foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies()){ var x=a.GetType(n); if(x!=null) return x; } return null; }
if (UnityEditor.EditorUtility.scriptCompilationFailed) return "COMPILE FAILED";
var t = Find("Laubrary.Shaper.Editor.ShaperFillAudit");
string r = (string)t.GetMethod("FT20_ContactSheet").Invoke(null, new object[]{ @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0106\fill-contact-sheet.png" });
System.IO.File.WriteAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0106\verify\ft20.txt", r);
return "ok";
