// Package-wide: does any text/numeric INPUT print a string wider than the box it prints it in?
// The inner TextElement of a UITK text field grows to its text and is then clipped by the input box,
// so the honest test is (inner text need) vs (the INPUT's content width) — not the text element's own.
string wn = UnityEditor.EditorPrefs.GetString("T324.win", "PyreWindow");
var win = ZWin(wn); if (win == null) return "no window " + wn;
var sb = new System.Text.StringBuilder();
int n = 0, bad = 0;
sb.AppendLine("window\tfieldType\tclasses\ttext\tneed\tinputHave\tover\tcaption");
foreach (var e in ZAll(win.rootVisualElement))
{
    var cl = ZCls(e);
    if (!cl.Contains("unity-base-text-field__input")) continue;
    if (!ZDrawn(e)) continue;
    UnityEngine.UIElements.TextElement te = null;
    foreach (var c in ZAll(e)) { var t = c as UnityEngine.UIElements.TextElement; if (t != null && !string.IsNullOrEmpty(t.text)) { te = t; break; } }
    if (te == null) continue;
    n++;
    float need = ZNeed(te), have = e.contentRect.width;
    if (need > have + ZTOL)
    {
        bad++;
        var owner = e.parent;
        sb.AppendLine(wn + "\t" + (owner != null ? owner.GetType().Name : "?") + "\t" + (owner != null ? ZCls(owner) : "")
            + "\t" + te.text + "\t" + need.ToString("0.##") + "\t" + have.ToString("0.##")
            + "\t" + (need - have).ToString("0.##") + "\t" + ZCaption(owner));
    }
}
string p = ZDump("fieldsweep-" + wn, sb.ToString());
return wn + ": " + n + " text inputs drawn, " + bad + " print more than they can show -> " + p + "\n" + sb.ToString();
