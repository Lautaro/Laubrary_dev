if (UnityEditor.EditorUtility.scriptCompilationFailed) return "COMPILE FAILED";
var t = System.Type.GetType("Laubrary.Shaper.Editor.ShaperFillAudit, com.Lautaro-Arino.Laubrary.Shaper.Editor");
var sb = new System.Text.StringBuilder();
sb.AppendLine((string)t.GetMethod("RunAll").Invoke(null, null));
sb.AppendLine((string)t.GetMethod("FT20_ContactSheet").Invoke(null, new object[]{ @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0106\fill-contact-sheet.png" }));
System.IO.File.WriteAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0106\FILL-AUDIT.txt", sb.ToString());
int pass = 0, fail = 0;
foreach (var line in sb.ToString().Split('\n')) { if (line.Contains("PASS")) pass++; if (line.Contains("FAIL")) fail++; }
return "written. lines-with-PASS=" + pass + " lines-with-FAIL=" + fail;
