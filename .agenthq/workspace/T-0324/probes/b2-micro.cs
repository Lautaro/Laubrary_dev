// Census every ZuiMicroSlider in a window: caption fit, in-track value readout fit, and the numeric
// input's own inner text element fit — the three places a MicroSlider can print a number.
string wn = UnityEditor.EditorPrefs.GetString("T324.win", "PyreWindow");
var win = ZWin(wn); if (win == null) return "no window " + wn;
var all = ZAll(win.rootVisualElement);
var sb = new System.Text.StringBuilder();
int n = 0, capShort = 0, valShort = 0, numShort = 0, numShown = 0;
sb.AppendLine("caption\townW\tcapNeed\tcapHave\tvalText\tvalNeed\tvalHave\tnumDisp\tnumText\tnumNeed\tnumHave");
foreach (var e in all)
{
    if (e.GetType().Name != "ZuiMicroSlider") continue;
    if (!ZDrawn(e)) continue;
    n++;
    UnityEngine.UIElements.Label cap = null, val = null;
    UnityEngine.UIElements.VisualElement num = null;
    foreach (var c in ZAll(e))
    {
        var cl = ZCls(c);
        if (cl.Contains("zui-microslider__caption") && cap == null) cap = c as UnityEngine.UIElements.Label;
        else if (cl.Contains("zui-microslider__value") && val == null) val = c as UnityEngine.UIElements.Label;
        else if (cl.Contains("zui-microslider__numfield") && num == null) num = c;
    }
    string capT = cap != null ? cap.text : "";
    float capN = cap != null ? ZNeed(cap) : 0f, capH = cap != null ? ZHave(cap) : 0f;
    bool capDisp = cap != null && cap.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.Flex;
    string valT = ""; float valN = 0f, valH = 0f; bool valDisp = false;
    if (val != null) { valT = val.text; valN = ZNeed(val); valH = ZHave(val); valDisp = val.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.Flex; }
    string numT = ""; float numN = 0f, numH = 0f; bool numDisp = false;
    if (num != null)
    {
        numDisp = num.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.Flex;
        foreach (var c in ZAll(num))
        {
            var te = c as UnityEngine.UIElements.TextElement;
            if (te == null || string.IsNullOrEmpty(te.text)) continue;
            numT = te.text; numN = ZNeed(te); numH = ZHave(te); break;
        }
    }
    if (capDisp && capN > capH + ZTOL) capShort++;
    if (valDisp && valN > valH + ZTOL) valShort++;
    if (numDisp) { numShown++; if (numN > numH + ZTOL) numShort++; }
    sb.AppendLine(capT + "\t" + e.resolvedStyle.width.ToString("0.##") + "\t" + capN.ToString("0.##") + "\t" + capH.ToString("0.##")
        + "\t" + valT + "\t" + valN.ToString("0.##") + "\t" + valH.ToString("0.##")
        + "\t" + numDisp + "\t" + numT + "\t" + numN.ToString("0.##") + "\t" + numH.ToString("0.##"));
}
string p = ZDump("micro-" + wn, sb.ToString());
return wn + ": " + n + " microsliders | captionShort=" + capShort + " valueShort=" + valShort
    + " | numeric inputs shown=" + numShown + " of which clipped=" + numShort + " -> " + p;
