// audit the Shaper window in whatever state it is in; dump the full report, return the summary.
string tag = UnityEditor.EditorPrefs.GetString("T336.tag", "shaper");
var w = ZWin("ShaperWindow");
if (w == null) return "NO WINDOW";
var rep = ZAudit(w, tag);
var p = ZDump("audit-" + tag + ".txt", rep);
return ZSummary(tag) + "dump=" + p + "\n";
