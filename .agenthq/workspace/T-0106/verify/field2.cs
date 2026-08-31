if (UnityEditor.EditorUtility.scriptCompilationFailed) return "COMPILE FAILED";
var t = System.Type.GetType("Laubrary.Shaper.Editor.ShaperFieldAudit, com.Lautaro-Arino.Laubrary.Shaper.Editor");
string[] names = System.IO.File.ReadAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0106\verify\chunk.txt").Trim().Split(',');
var sb = new System.Text.StringBuilder();
foreach (var nm in names) sb.AppendLine((string)t.GetMethod(nm.Trim()).Invoke(null, null));
System.IO.File.AppendAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0106\verify\FIELD-AUDIT.txt", sb.ToString());
int pass = 0, fail = 0;
foreach (var line in sb.ToString().Split('\n')) { if (line.Contains("RESULT: PASS")) pass++; if (line.Contains("RESULT: FAIL")) fail++; }
return "chunk done: RESULT PASS=" + pass + " FAIL=" + fail;
