var sb = new System.Text.StringBuilder();
var pw = ZWin("PyreWindow");
var seen = new System.Collections.Generic.Dictionary<string,int>();
int cells = 0, tooWide = 0, elided = 0;
foreach (var e in ZAll(pw.rootVisualElement))
{
    if (!ZDrawn(e)) continue;
    if (!ZCls(e).Contains("zui-cell__name")) continue;
    var te = e as UnityEngine.UIElements.TextElement; if (te == null) continue;
    cells++;
    if (te.text.Contains("\u2026")) elided++;
    if (ZNeed(te) > ZHave(te) + 1.5f) tooWide++;
    if (!seen.ContainsKey(te.text)) seen[te.text] = 0;
    seen[te.text]++;
}
int dupes = 0; var dl = new System.Text.StringBuilder();
foreach (var kv in seen) if (kv.Value > 1) { dupes++; dl.Append(" '").Append(kv.Key).Append("'x").Append(kv.Value); }
sb.Append("Pyre library cells=").Append(cells).Append(" elided=").Append(elided).Append(" tooWide=").Append(tooWide)
  .Append(" duplicateVisibleNames=").Append(dupes).Append(dl).Append("\n");
int shown = 0;
foreach (var e in ZAll(pw.rootVisualElement))
{ if (!ZDrawn(e) || !ZCls(e).Contains("zui-cell__name")) continue; var te = e as UnityEngine.UIElements.TextElement;
  if (te != null && te.text.Contains("\u2026") && shown++ < 8) sb.Append("   '").Append(te.text).Append("'\n"); }
return sb.ToString();
