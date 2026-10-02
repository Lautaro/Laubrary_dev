var win = ZWin("ShaperWindow"); var sb=new System.Text.StringBuilder();
int raw=0;
foreach (var e in ZAll(win.rootVisualElement)) {
  if (!(e is UnityEngine.UIElements.Toggle t)) continue;
  raw++;
  sb.Append(ZDrawn(e)?"* ":"  ").Append("Toggle '").Append(t.label).Append("'/'").Append(t.text).Append("' cls=").Append(ZCls(e))
    .Append(" tip='").Append(ZTip(e)).Append("' wb=").Append(e.worldBound).Append(" path=").Append(ZPath(e)).Append("\n");
}
sb.Insert(0,"rawToggles="+raw+"\n");
return sb.ToString();
