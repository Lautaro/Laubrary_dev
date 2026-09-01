if (UnityEditor.EditorUtility.scriptCompilationFailed) return "COMPILE FAILED";
var t = System.Type.GetType("Laubrary.Shaper.Editor.ShaperFieldAudit, com.Lautaro-Arino.Laubrary.Shaper.Editor");
string s = (string)t.GetMethod("RunAll").Invoke(null, null);
System.IO.File.WriteAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0106\verify\FIELD-AUDIT.txt", s);
int pass = 0, fail = 0;
foreach (var line in s.Split('\n')) {
  if (line.Contains("RESULT: PASS")) pass++;
  if (line.Contains("RESULT: FAIL")) fail++;
}
return "field audit: RESULT PASS=" + pass + " FAIL=" + fail + "  (chars " + s.Length + ")";
