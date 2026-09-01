string outDir = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0109\v3\run5\";
System.IO.Directory.CreateDirectory(outDir);
var sb = new System.Text.StringBuilder();
sb.AppendLine("dataPath=" + UnityEngine.Application.dataPath);
sb.AppendLine("compileFailed=" + UnityEditor.EditorUtility.scriptCompilationFailed);
System.Type ht = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var x = a.GetType("Laubrary.Shaper.ShaperHeight"); if (x != null) { ht = x; break; } }
sb.AppendLine("InverseAtEUpperBound present = " + (ht != null && ht.GetMethod("InverseAtEUpperBound", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static) != null));
string[] names = { "ShaperFieldAudit", "ShaperFillAudit", "ShaperBorderAudit", "ShaperLightAudit", "ShaperHeightAudit" };
foreach (string n in names)
{
    System.Type t = null;
    foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies()) { var x = asm.GetType("Laubrary.Shaper.Editor." + n); if (x != null) { t = x; break; } }
    if (t == null) { sb.AppendLine(n + ": TYPE NOT FOUND"); continue; }
    var m = t.GetMethod("RunAll", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
    if (m == null) { sb.AppendLine(n + ": RunAll NOT FOUND"); continue; }
    var ps = m.GetParameters();
    object[] args = new object[ps.Length];
    for (int i = 0; i < ps.Length; i++) args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
    var sw = System.Diagnostics.Stopwatch.StartNew();
    string res;
    try { res = (string)m.Invoke(null, args); }
    catch (System.Exception ex) { res = "EXCEPTION: " + ex.ToString(); }
    sw.Stop();
    System.IO.File.WriteAllText(outDir + n + ".txt", res);
    int pass = 0, fail = 0, vOk = 0, vTot = 0;
    foreach (var line in res.Split('\n'))
    {
        if (line.Contains("PASS")) pass++;
        if (line.Contains("FAIL")) fail++;
        if (line.Contains("VERDICT")) { vTot++; if (line.Contains("ok")) vOk++; }
    }
    sb.AppendLine(n + ": " + sw.ElapsedMilliseconds + " ms  PASS=" + pass + "  FAIL=" + fail + "  VERDICT ok " + vOk + "/" + vTot);
    System.IO.File.WriteAllText(outDir + "SUMMARY.txt", sb.ToString());
}
sb.AppendLine("ALLDONE");
System.IO.File.WriteAllText(outDir + "SUMMARY.txt", sb.ToString());
return sb.ToString();
