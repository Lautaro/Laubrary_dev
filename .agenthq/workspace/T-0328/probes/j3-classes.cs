var sb = new System.Text.StringBuilder();
var w = ZWin("ChoreographerWindow");
foreach (var e in ZAll(w.rootVisualElement))
{
    if (!ZDrawn(e)) continue;
    var te = e as UnityEngine.UIElements.TextElement; if (te == null || string.IsNullOrEmpty(te.text)) continue;
    if (te.text.Contains("Spread") || te.text.Contains("Preview —") || te.text.Contains("Anchors"))
        sb.Append("'").Append(te.text).Append("' cls=").Append(ZCls(te)).Append(" tip=").Append(ZTip(te).Length==0?"<NONE>":"yes").Append(" path=").Append(ZPath(te)).Append("\n");
}
return sb.ToString();
