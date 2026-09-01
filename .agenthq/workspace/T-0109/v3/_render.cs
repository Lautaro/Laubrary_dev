string outDir = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0109\v3\run5\";
System.IO.Directory.CreateDirectory(outDir);
System.Type t = null;
foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies()) { var x = asm.GetType("Laubrary.Shaper.Editor.ShaperHeightAudit"); if (x != null) { t = x; break; } }
var m = t.GetMethod("RunAll", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
string res = (string)m.Invoke(null, new object[]{ outDir + "v3-contact-sheet.png", outDir + "v3-tilted.png" });
System.IO.File.WriteAllText(outDir + "ShaperHeightAudit_render.txt", res);
System.IO.File.WriteAllText(outDir + "RENDER.done", "done " + UnityEngine.Application.dataPath);
return "started";
