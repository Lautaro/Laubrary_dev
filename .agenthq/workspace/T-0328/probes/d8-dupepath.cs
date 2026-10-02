var sb = new System.Text.StringBuilder();
foreach (var wn in new string[]{"PyreWindow","PropWindow"})
{
  var w = ZWin(wn); sb.Append("== ").Append(wn).Append(" ==\n");
  foreach (var e in ZAll(w.rootVisualElement))
  {
    if (!ZDrawn(e)) continue;
    var cls = ZCls(e);
    if (!(cls.Contains("zui-box__title") || cls.Contains("zui-section__title") || cls.Contains("zui-box__header-title"))) continue;
    var te = e as UnityEngine.UIElements.TextElement; if (te == null) continue;
    if (te.text != "Adjust" && te.text != "Tags") continue;
    sb.Append("  '").Append(te.text).Append("' y=").Append(te.worldBound.y.ToString("F0"))
      .Append(" tip=").Append(ZTip(te).Length > 90 ? ZTip(te).Substring(0,90) : ZTip(te)).Append("\n")
      .Append("    path=").Append(ZPath(te)).Append("\n");
  }
}
return sb.ToString();
