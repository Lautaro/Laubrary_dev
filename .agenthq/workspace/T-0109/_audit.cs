// T-0109 — run the whole height audit (H1..H10) plus both renders, and write HEIGHT-AUDIT.txt.
// Re-run with:
//   unity command --project-path "D:/UNITY/Laubrary Dev - Shaper" --timeout 900 eval_file --file <this>
// Fully qualified everywhere: the CLI's eval_file wraps this in a method body, so `using` aliases at the
// top silently fail (T-0105's gotcha).
string dir = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0109\";
string txt = Laubrary.Shaper.Editor.ShaperHeightAudit.RunAll(dir + "height-contact-sheet.png",
                                                             dir + "tilted-conformance.png");
System.IO.File.WriteAllText(dir + "HEIGHT-AUDIT.txt", txt);
return "WROTE " + dir + "HEIGHT-AUDIT.txt\n" + txt;
