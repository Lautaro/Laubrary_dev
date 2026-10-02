// T-0313 — the shared four-audit pass over ANY window (T313.win), tagged T0312.tag. Same ZAudit as every
// other probe in this programme; only the window lookup differs.
string wname = UnityEditor.EditorPrefs.GetString("T313.win", "ChunkWindow");
string tag = UnityEditor.EditorPrefs.GetString("T0312.tag", wname);
var win = ZWin(wname);
var report = ZAudit(win, tag);
var path = ZDump("audit-" + tag + ".txt", report);
var sb = new System.Text.StringBuilder();
sb.Append(path).Append("\n").Append(ZSummary(tag));
int a = report.IndexOf("-- captions --");
int b = report.IndexOf("-- controls with no effective tooltip --");
int c = report.IndexOf("-- every leaf control --");
if (a >= 0 && b > a) sb.Append(report.Substring(a, b - a));
if (b >= 0 && c > b) sb.Append(report.Substring(b, System.Math.Min(c - b, 6000)));
return sb.ToString();
