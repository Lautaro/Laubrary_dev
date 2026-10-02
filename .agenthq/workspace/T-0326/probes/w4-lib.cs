// Force a ZuiAssetWindow's library browser open and measure every cell name: does the string fit the
// cell, and if not, what does the TAIL ellipsis leave — do two different assets end up reading the same?
string names = UnityEditor.EditorPrefs.GetString("T326.wins", "");
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
var sb = new System.Text.StringBuilder();
foreach (var n in names.Split('|'))
{
    if (string.IsNullOrEmpty(n)) continue;
    var w = ZWin(n); if (w == null) { sb.Append(n).Append(": NOT OPEN\n"); continue; }
    // browsing already forced on by w4a-browse.cs
    w.rootVisualElement.MarkDirtyRepaint(); w.Repaint();
    // force a layout pass
    var pan = w.rootVisualElement.panel;
    int cells = 0, over = 0;
    var seen = new System.Collections.Generic.Dictionary<string,string>();
    var lines = new System.Text.StringBuilder();
    var vis = new System.Collections.Generic.List<string>();
    foreach (var e in ZAll(w.rootVisualElement))
    {
        if (!ZDrawn(e)) continue;
        if (!ZCls(e).Contains("zui-cell__name")) continue;
        var te = e as UnityEngine.UIElements.TextElement; if (te == null) continue;
        cells++;
        float need = ZNeed(te), have = ZHave(te);
        string shown = te.text;
        if (need > have + 0.5f)
        {
            over++;
            // simulate UITK's TAIL ellipsis: longest prefix + "…" that fits
            for (int k = te.text.Length - 1; k >= 0; k--)
            {
                string cand = te.text.Substring(0, k) + "…";
                var probe = new UnityEngine.UIElements.Label(cand);
                float wpx = te.MeasureTextSize(cand, 0, UnityEngine.UIElements.VisualElement.MeasureMode.Undefined, 0, UnityEngine.UIElements.VisualElement.MeasureMode.Undefined).x;
                if (wpx <= have) { shown = cand; break; }
            }
        }
        lines.Append("   '").Append(te.text).Append("' need=").Append(need.ToString("F1")).Append(" have=").Append(have.ToString("F1")).Append(" shows='").Append(shown).Append("'\n");
        vis.Add(shown + "\t" + te.text);
    }
    int coll = 0, collTrunc = 0;
    foreach (var v in vis)
    {
        string s = v.Split('\t')[0], full = v.Split('\t')[1];
        if (seen.ContainsKey(s)) { coll++; if (seen[s] != full) collTrunc++; }
        else seen[s] = full;
    }
    sb.Append(n).Append(": cells=").Append(cells).Append(" tooWide=").Append(over)
      .Append(" visibleCollisions=").Append(coll).Append(" ofWhichTruncationCaused=").Append(collTrunc).Append("\n").Append(lines);
}
string p = ZDump("lib-" + UnityEditor.EditorPrefs.GetString("T326.tag","x"), sb.ToString());
return sb.Length > 6000 ? sb.ToString().Substring(0, 6000) + "\n...(-> " + p + ")" : sb.ToString() + "-> " + p;
