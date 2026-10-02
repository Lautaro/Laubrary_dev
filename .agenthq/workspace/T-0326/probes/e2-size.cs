// Find Pyre's "Size" MicroSlider, scroll it into view, and report the numeric input's geometry.
UnityEditor.EditorPrefs.SetString("T326.win", "PyreWindow");
UnityEditor.EditorPrefs.SetString("T326.find", "Size");
var win = ZWin("PyreWindow"); if (win == null) return "no Pyre window";
var sb = new System.Text.StringBuilder();
UnityEngine.UIElements.VisualElement hit = null;
foreach (var e in ZAll(win.rootVisualElement))
{
    if (e.GetType().Name != "ZuiMicroSlider" || !ZDrawn(e)) continue;
    UnityEngine.UIElements.Label cap = null;
    foreach (var c in ZAll(e)) { if (ZCls(c).Contains("zui-microslider__caption")) { cap = c as UnityEngine.UIElements.Label; break; } }
    if (cap == null || cap.text != "Size") continue;
    hit = e; break;
}
if (hit == null) return "no Size MicroSlider";
UnityEngine.UIElements.ScrollView sv = null;
for (var p = hit.hierarchy.parent; p != null; p = p.hierarchy.parent) { var s = p as UnityEngine.UIElements.ScrollView; if (s != null) { sv = s; break; } }
if (sv != null) sv.ScrollTo(hit);
win.Repaint();
sb.Append("slider rect=").Append(hit.worldBound).Append("\n");
foreach (var c in ZAll(hit))
{
    var cls = ZCls(c);
    if (cls.Contains("zui-microslider__value") || cls.Contains("zui-microslider__num") || c is UnityEngine.UIElements.FloatField)
    {
        sb.Append(c.GetType().Name).Append(" cls=").Append(cls).Append(" rect=").Append(c.worldBound).Append(" content=").Append(c.contentRect.width.ToString("F1"));
        var te = c as UnityEngine.UIElements.TextElement;
        if (te != null) sb.Append(" text='").Append(te.text).Append("' need=").Append(ZNeed(te).ToString("F1")).Append(" have=").Append(ZHave(te).ToString("F1"));
        sb.Append("\n");
        foreach (var d in ZAll(c)) { var t2 = d as UnityEngine.UIElements.TextElement; if (t2 != null && !string.IsNullOrEmpty(t2.text)) sb.Append("   inner '").Append(t2.text).Append("' need=").Append(ZNeed(t2).ToString("F1")).Append(" have=").Append(ZHave(t2).ToString("F1")).Append(" rect=").Append(t2.worldBound).Append("\n"); }
    }
}
return sb.ToString();
