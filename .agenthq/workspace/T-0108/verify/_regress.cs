var asm = System.Reflection.Assembly.Load("com.Lautaro-Arino.Laubrary.Shaper.Editor");
string which = System.IO.File.ReadAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0108\verify\leg.txt").Trim();
var t = asm.GetType("Laubrary.Shaper.Editor." + which);
var m = t.GetMethod("RunAll", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
string res;
try { res = (string)m.Invoke(null, null); }
catch (System.Exception e) { res = which + " EXCEPTION: " + (e.InnerException ?? e).ToString(); }
System.IO.File.AppendAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0108\REGRESSION.txt", res + "\n\n");
return which + " done";
