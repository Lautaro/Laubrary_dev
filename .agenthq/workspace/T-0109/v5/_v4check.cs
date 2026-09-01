string outDir = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0109\v5\";
System.IO.Directory.CreateDirectory(outDir);
var sb = new System.Text.StringBuilder();
sb.AppendLine("dataPath=" + UnityEngine.Application.dataPath);
sb.AppendLine("compileFailed=" + UnityEditor.EditorUtility.scriptCompilationFailed);
System.Type t = null;
foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies()) { var x = asm.GetType("Laubrary.Shaper.Editor.ShaperHeightAudit"); if (x != null) { t = x; break; } }
sb.AppendLine("type=" + (t != null));
string[] ms = { "H12_RegressionTraps", "H6_BranchAgreement" };
foreach (string mn in ms)
{
    var m = t.GetMethod(mn, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    string res;
    try { res = (string)m.Invoke(null, null); } catch (System.Exception ex) { res = "EXCEPTION " + ex.ToString(); }
    sw.Stop();
    sb.AppendLine("### " + mn + "  " + sw.ElapsedMilliseconds + " ms");
    sb.AppendLine(res);
    System.IO.File.WriteAllText(outDir + "V4-CHECK.txt", sb.ToString());
}
System.IO.File.WriteAllText(outDir + "V4-CHECK.txt", sb.ToString());
return sb.ToString();
