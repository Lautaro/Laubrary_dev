var t = System.Type.GetType("Laubrary.Shaper.Editor.ShaperLightAudit, com.Lautaro-Arino.Laubrary.Shaper.Editor");
string leg = System.IO.File.ReadAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0108\verify\leg.txt").Trim();
var m = t.GetMethod(leg, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
string res;
try { res = (string)m.Invoke(null, m.GetParameters().Length == 0 ? null : new object[]{ @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0108\light-contact-sheet.png" }); }
catch (System.Exception e) { res = leg + "\n  EXCEPTION: " + (e.InnerException ?? e).ToString() + "\n  RESULT: FAIL"; }
System.IO.File.AppendAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0108\LIGHT-AUDIT.txt", res + "\n\n");
return leg + " done";
