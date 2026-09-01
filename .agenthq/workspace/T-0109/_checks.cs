// T-0109 — the numeric checks only (no renders), for a fast iteration loop.
string dir = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0109\";
string txt = Laubrary.Shaper.Editor.ShaperHeightAudit.RunAll(null, null);
System.IO.File.WriteAllText(dir + "HEIGHT-AUDIT.txt", txt);
return txt;
