// T-0313 — what the compact row's spill DOES to the user, as opposed to how many px it is.
//   (a) is the spilled part inside the dial pane's clipping viewport at all (if not: invisible AND
//       unclickable — a control the user cannot reach), and
//   (b) does it land on top of another drawn control (overlap: two dials in the same pixels).
var win = ZWin("PyreWindow");
if (win == null) return "NO PYRE WINDOW";
var all = ZAll(win.rootVisualElement);
var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append(" window=").Append(win.position).Append("\n");

UnityEngine.UIElements.VisualElement viewport = null;
foreach (var e in all) if (e.name == "unity-content-viewport" && ZDrawn(e)) { viewport = e; break; }
var vp = viewport == null ? new UnityEngine.Rect(0, 0, 0, 0) : viewport.worldBound;
sb.Append("dial-pane viewport = ").Append(vp.xMin.ToString("F1")).Append("..").Append(vp.xMax.ToString("F1")).Append("\n");

foreach (var e in all)
{
    if (!ZDrawn(e)) continue;
    int n = e.hierarchy.childCount;
    if (n < 3) continue;
    var last = e.hierarchy[n - 1];
    if (!(last is UnityEngine.UIElements.Button) || ZOwnText(last) != "×") continue;
    var par = e.hierarchy.parent; var pc = ZContentWorld(par);
    for (int i = 0; i < n; i++)
    {
        var c = e.hierarchy[i]; var b = c.worldBound;
        if (b.xMax <= pc.xMax + ZTOL) continue;
        bool visible = b.xMax <= vp.xMax + 0.5f;
        sb.Append("SPILLED '").Append(ZCaption(c)).Append("' ").Append(c.GetType().Name)
          .Append(" x=").Append(b.xMin.ToString("F1")).Append("..").Append(b.xMax.ToString("F1"))
          .Append(visible ? "  inside the pane viewport" : "  OUTSIDE the pane viewport (clipped: invisible + unclickable)");
        // overlap: any OTHER drawn leaf control whose rect intersects this one
        int overlaps = 0; string first = "";
        foreach (var o in all)
        {
            if (o == c || !ZDrawn(o) || !ZIsLeafCtrl(o)) continue;
            bool anc = false; for (var p = o; p != null; p = p.hierarchy.parent) if (p == c) { anc = true; break; }
            if (anc) continue;
            for (var p = c; p != null; p = p.hierarchy.parent) if (p == o) { anc = true; break; }
            if (anc) continue;
            if (o.worldBound.Overlaps(b)) { overlaps++; if (first == "") first = ZCaption(o) + " " + o.worldBound.ToString(); }
        }
        sb.Append("  overlapsOtherControls=").Append(overlaps);
        if (overlaps > 0) sb.Append(" first='").Append(first).Append("'");
        sb.Append("\n");
    }
    break;
}
var path = ZDump("spill-" + UnityEditor.EditorPrefs.GetString("T0312.tag", "x") + ".txt", sb.ToString());
return path + "\n" + sb.ToString();
