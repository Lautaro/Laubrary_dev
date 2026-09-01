var asm = System.Reflection.Assembly.Load("com.Lautaro-Arino.Laubrary.Shaper.Editor");
var t = asm.GetType("Laubrary.Shaper.Editor.ShaperFieldAudit");
var m = t.GetMethod("RunAll", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, System.Type.EmptyTypes, null);
string res;
try { res = (string)m.Invoke(null, null); }
catch (System.Exception e) { res = "ShaperFieldAudit EXCEPTION: " + (e.InnerException ?? e).ToString(); }
System.IO.File.AppendAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0108\REG_Field.txt", res);
return "ShaperFieldAudit done";
