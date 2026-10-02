// What a person SEES: every drawn text and every drawn control, in layout order.
string wn = UnityEditor.EditorPrefs.GetString("T324.win", "");
var w = ZWin(wn); if (w == null) return "no " + wn;
var sb = new System.Text.StringBuilder();
var list = ZAll(w.rootVisualElement);
list.Sort((a,b) => { int c = a.worldBound.y.CompareTo(b.worldBound.y); return c != 0 ? c : a.worldBound.x.CompareTo(b.worldBound.x); });
foreach (var e in list)
{
    if (!ZDrawn(e)) continue;
    var te = e as UnityEngine.UIElements.TextElement;
    bool ctl = ZIsLeafCtrl(e);
    if (te == null && !ctl) continue;
    string t = te != null ? te.text : ZCaption(e);
    if (string.IsNullOrEmpty(t)) t = "<" + e.GetType().Name + ">";
    sb.AppendLine((ctl ? (e.enabledInHierarchy ? "[BTN] " : "[GREY] ") : "      ") + t
      + (ctl && !e.enabledInHierarchy ? "   why: " + ZTip(e) : ""));
}
return ZDump("read-" + wn, sb.ToString()) + "\n" + sb.ToString();
