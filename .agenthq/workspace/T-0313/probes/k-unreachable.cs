// T-0313 — every drawn leaf control in T313.win that cannot take keyboard focus at all (focusable=false
// or canGrabFocus=false), grouped by control type: the population Tab can never reach.
string wname = UnityEditor.EditorPrefs.GetString("T313.win", "ShaperWindow");
var win = ZWin(wname);
if (win == null) return "NO WINDOW " + wname;
var sb = new System.Text.StringBuilder();
var byType = new System.Collections.Generic.Dictionary<string, int>();
var reach = new System.Collections.Generic.Dictionary<string, int>();
int leaves = 0, unreachable = 0;
foreach (var e in ZAll(win.rootVisualElement))
{
    if (!ZDrawn(e) || !ZIsLeafCtrl(e)) continue;
    leaves++;
    var n = e.GetType().Name;
    if (e.focusable)
    {
        int r; reach.TryGetValue(n, out r); reach[n] = r + 1;
        continue;
    }
    unreachable++;
    int c; byType.TryGetValue(n, out c); byType[n] = c + 1;
    if (byType[n] <= 3)
        sb.Append("  UNREACHABLE '").Append(ZCaption(e)).Append("' ").Append(n)
          .Append(" enabled=").Append(e.enabledInHierarchy).Append(" focusable=").Append(e.focusable)
          .Append(" at ").Append(e.worldBound.xMin.ToString("F0")).Append(",").Append(e.worldBound.yMin.ToString("F0"))
          .Append(" | ").Append(ZPath(e)).Append("\n");
}
var head = new System.Text.StringBuilder();
head.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append(" ").Append(wname)
    .Append(" window=").Append(win.position.width.ToString("F0"))
    .Append(" leafControls=").Append(leaves).Append(" not-focusable=").Append(unreachable).Append("\n");
foreach (var kv in byType) head.Append("  ").Append(kv.Key).Append(" x").Append(kv.Value).Append(" UNREACHABLE\n");
foreach (var kv in reach) head.Append("  ").Append(kv.Key).Append(" x").Append(kv.Value).Append(" focusable\n");
var text = head.ToString() + sb.ToString();
return ZDump("unreachable-" + UnityEditor.EditorPrefs.GetString("T0312.tag", wname) + ".txt", text) + "\n" + text;
