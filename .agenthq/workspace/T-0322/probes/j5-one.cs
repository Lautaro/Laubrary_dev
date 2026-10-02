var sb=new System.Text.StringBuilder();
var w = ZWin("ZoeWindow");
foreach (var e in ZAll(w.rootVisualElement)) {
  var tg = e as UnityEngine.UIElements.Toggle; if (tg==null || !ZDisplayed(tg)) continue;
  sb.Append("Toggle cap='").Append(ZCaption(tg)).Append("' cls=").Append(ZCls(tg)).Append(" allow=").Append(tg.ClassListContains("zui-audit-allow-toggle")).Append(" path=").Append(ZPath(tg)).Append("\n"); }
return sb.ToString();
