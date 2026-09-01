// T-0109 FIXER — run ONE audit check and write it to its own file. The unity CLI's eval_file pipeline has a
// HARD 30 s timeout that --timeout does not override, so the audit is run in pieces and concatenated.
// Fully qualified throughout: eval_file wraps this in a method body and `using` aliases silently fail.
string which = System.IO.File.ReadAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0109\parts\WHICH.txt").Trim();
var t = typeof(Laubrary.Shaper.Editor.ShaperHeightAudit);
string outp = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0109\parts\" + which + ".txt";
var sw = System.Diagnostics.Stopwatch.StartNew();
string res;
if (which == "HeightContactSheet")
    res = Laubrary.Shaper.Editor.ShaperHeightAudit.HeightContactSheet(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0109\height-contact-sheet.png");
else if (which == "TiltedConformance")
    res = Laubrary.Shaper.Editor.ShaperHeightAudit.TiltedConformance(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0109\tilted-conformance.png");
else
    res = (string)t.GetMethod(which).Invoke(null, null);
sw.Stop();
System.IO.File.WriteAllText(outp, res);
return which + " -> " + outp + "   " + sw.ElapsedMilliseconds + " ms, " + res.Length + " chars";
