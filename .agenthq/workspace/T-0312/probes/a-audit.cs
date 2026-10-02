// Runs all four audits (captions / overflow / tooltips / inert-without-reason) over the window named by
// EditorPrefs "T0312.unit" ("shaper" | "pyre"), dumps the full report to T0312.out under the name in
// "T0312.tag", and returns the counts plus the caption+overflow+inert findings (the actionable ones).
string unit = UnityEditor.EditorPrefs.GetString("T0312.unit", "shaper");
string tag  = UnityEditor.EditorPrefs.GetString("T0312.tag", unit);
var win = ZWin(unit == "pyre" ? "PyreWindow" : "ShaperWindow");
var report = ZAudit(win, tag);
var path = ZDump("audit-" + tag + ".txt", report);

// The laid-out ZuiColumnFlow column count — read HERE, in a later call than the one that set the pane
// width, because a flow redistributes on the GeometryChangedEvent that follows the rebuild, so a
// same-call read always reports the pre-layout count (1).
int cols = 0; float flowW = 0f;
if (win != null)
    foreach (var v in ZAll(win.rootVisualElement))
        if (v.GetType().Name == "ZuiColumnFlow")
        { flowW = v.worldBound.width; cols = v.hierarchy.childCount > 0 ? v.hierarchy[0].hierarchy.childCount : 0; break; }

var sb = new System.Text.StringBuilder();
sb.Append(path).Append("\n").Append("columns=").Append(cols).Append(" flowWidth=").Append(flowW.ToString("F0")).Append("\n")
  .Append(ZSummary(tag));
int cut = report.IndexOf("-- controls with no effective tooltip --");
int cut2 = report.IndexOf("-- every leaf control --");
int cut3 = report.IndexOf("-- disabled without a reason --");
if (cut > 0) sb.Append(report.Substring(report.IndexOf("-- captions --"), cut - report.IndexOf("-- captions --")));
if (cut3 > 0 && cut2 > cut3) sb.Append(report.Substring(cut3, cut2 - cut3));
return sb.ToString();
