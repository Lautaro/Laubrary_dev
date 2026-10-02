var win = ZWin("ShaperWindow"); var vt = win.rootVisualElement.panel.visualTree;
UnityEngine.UIElements.VisualElement pop = null;
foreach (var e in ZAll(vt)) if ((ZCls(e)??"") == "zui-popover" || (ZCls(e)??"").StartsWith("zui-popover.") || (ZCls(e)??"").EndsWith(".zui-popover") || (ZCls(e)??"").Contains(".zui-popover.")) pop = e;
if (pop==null) foreach (var e in ZAll(vt)) { var cl=ZCls(e)??""; if (cl.Contains("zui-popover") && !cl.Contains("scrim")) pop = e; }
if (pop==null) return "no popover open";
var sb=new System.Text.StringBuilder();
sb.Append("popover ").Append(pop.worldBound).Append("\n");
foreach (var e in ZAll(pop)) {
  if (!ZDrawn(e)) continue;
  if (!ZIsCtrl(e)) continue;
  sb.Append(e.GetType().Name).Append(" '").Append(ZOwnText(e)).Append("' cap='").Append(ZCaption(e)).Append("' tip='").Append(ZTip(e)).Append("' en=").Append(e.enabledInHierarchy).Append(" wb=").Append(e.worldBound).Append("\n");
}
return sb.ToString();
