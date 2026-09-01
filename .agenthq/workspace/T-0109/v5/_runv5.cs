string outDir = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0109\v5\run\";
string wsDir  = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0109\";
System.IO.Directory.CreateDirectory(outDir);
var sb = new System.Text.StringBuilder();
sb.AppendLine("T-0109 v5 FULL AUDIT RUN  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
sb.AppendLine("dataPath=" + UnityEngine.Application.dataPath);
sb.AppendLine("compileFailed=" + UnityEditor.EditorUtility.scriptCompilationFailed);
System.IO.File.WriteAllText(outDir + "SUMMARY.txt", sb.ToString());
string[] names = { "ShaperFieldAudit", "ShaperFillAudit", "ShaperBorderAudit", "ShaperLightAudit", "ShaperHeightAudit" };
foreach (string n in names)
{
    System.Type t = null;
    foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies()) { var x = asm.GetType("Laubrary.Shaper.Editor." + n); if (x != null) { t = x; break; } }
    if (t == null) { sb.AppendLine(n + ": TYPE NOT FOUND"); System.IO.File.WriteAllText(outDir + "SUMMARY.txt", sb.ToString()); continue; }
    var m = t.GetMethod("RunAll", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
    if (m == null) { sb.AppendLine(n + ": RunAll NOT FOUND"); System.IO.File.WriteAllText(outDir + "SUMMARY.txt", sb.ToString()); continue; }
    var ps = m.GetParameters();
    object[] args = new object[ps.Length];
    for (int i = 0; i < ps.Length; i++) args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
    if (n == "ShaperHeightAudit" && ps.Length == 2)
    {
        args[0] = wsDir + "height-contact-sheet.png";
        args[1] = wsDir + "tilted-conformance.png";
    }
    var sw = System.Diagnostics.Stopwatch.StartNew();
    string res;
    try { res = (string)m.Invoke(null, args); }
    catch (System.Exception ex) { res = "EXCEPTION: " + ex.ToString(); }
    sw.Stop();
    System.IO.File.WriteAllText(outDir + n + ".txt", res);
    if (n == "ShaperHeightAudit") System.IO.File.WriteAllText(wsDir + "HEIGHT-AUDIT.txt", res);
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
