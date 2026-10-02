// The numeric input's real geometry: field box vs its inner input vs the text it prints, and whether the
// text is clipped by the field.  Plus which zui.microslider.*.num prefs are currently ON.
string wn = UnityEditor.EditorPrefs.GetString("T324.win", "PyreWindow");
var win = ZWin(wn); if (win == null) return "no window " + wn;
var sb = new System.Text.StringBuilder();
foreach (var e in ZAll(win.rootVisualElement))
{
    if (e.GetType().Name != "ZuiMicroSlider" || !ZDrawn(e)) continue;
    UnityEngine.UIElements.VisualElement num = null; UnityEngine.UIElements.Label cap = null;
    foreach (var c in ZAll(e))
    {
        var cl = ZCls(c);
        if (cl.Contains("zui-microslider__numfield") && num == null) num = c;
        if (cl.Contains("zui-microslider__caption") && cap == null) cap = c as UnityEngine.UIElements.Label;
    }
    if (num == null || num.resolvedStyle.display != UnityEngine.UIElements.DisplayStyle.Flex) continue;
    sb.AppendLine("── " + (cap != null ? cap.text : "?") + "  slider w=" + e.resolvedStyle.width.ToString("0.##")
        + " wb=" + e.worldBound);
    sb.AppendLine("   numfield w=" + num.resolvedStyle.width.ToString("0.##") + " wb=" + num.worldBound
       );
    foreach (var c in ZAll(num))
    {
        var te = c as UnityEngine.UIElements.TextElement;
        string t = te != null ? te.text : null;
        sb.AppendLine("   · " + c.GetType().Name + " [" + ZCls(c) + "] wb=" + c.worldBound
            + " cw=" + c.contentRect.width.ToString("0.##")
            + (te != null ? " text='" + t + "' need=" + ZNeed(te).ToString("0.##") : "")
           );
    }
    // does the printed text fit inside the FIELD's own box?
    float rightEdge = num.worldBound.xMax;
    sb.AppendLine("   slider xMax=" + e.worldBound.xMax.ToString("0.##") + " field xMax=" + rightEdge.ToString("0.##"));
}
sb.AppendLine();
sb.AppendLine("prefs zui.microslider.*.num currently true:");
foreach (var lbl in new string[]{ "Size","Quantise","Speed","Width","Height","Hue","Alpha","Edge","Zoom","Delay","GIF scale","Phase","Contrast","Brightness","Saturation","Size (px)","Spin °" })
{
    string k = "zui.microslider.lbl:" + lbl + ".num";
    if (UnityEditor.EditorPrefs.GetBool(k, false)) sb.AppendLine("  ON  " + k);
    string k2 = "zui.microslider.lbl:" + lbl + ".val";
    if (!UnityEditor.EditorPrefs.GetBool(k2, true)) sb.AppendLine("  OFF " + k2);
}
return ZDump("numgeom-" + wn, sb.ToString()) + "\n" + sb.ToString();
