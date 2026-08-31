// T-0109 second fix pass — the four EARLIER-WAVE audits, run unchanged, to prove N1-N7 broke nothing.
string dir = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0109\";
var sb = new System.Text.StringBuilder();
System.Action<string, System.Func<string>> R = (name, f) =>
{
    string t;
    try { t = f(); } catch (System.Exception e) { t = "THREW: " + e; }
    System.IO.File.WriteAllText(dir + "_p2_" + name + ".txt", t);
    int pass = 0, fail = 0;
    foreach (var line in t.Split('\n'))
    {
        if (line.Contains("FAIL")) fail++;
        if (line.Contains("PASS")) pass++;
    }
    sb.AppendLine(name + ": PASS lines=" + pass + "  FAIL lines=" + fail + "  chars=" + t.Length);
};
R("field",  () => Laubrary.Shaper.Editor.ShaperFieldAudit.RunAll());
R("fill",   () => Laubrary.Shaper.Editor.ShaperFillAudit.RunAll());
R("border", () => Laubrary.Shaper.Editor.ShaperBorderAudit.RunAll());
R("light",  () => Laubrary.Shaper.Editor.ShaperLightAudit.RunAll());
return sb.ToString();
