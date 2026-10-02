var win = ZWin("ShaperWindow"); var vt = win.rootVisualElement.panel.visualTree;
UnityEngine.UIElements.VisualElement pop = null;
foreach (var e in ZAll(vt)) { var cl=ZCls(e)??""; if (cl.Contains("zui-popover") && !cl.Contains("scrim")) pop = e; }
if (pop==null) return "no popover";
var sb=new System.Text.StringBuilder();
foreach (var e in ZAll(pop)) sb.Append(ZDrawn(e)?"* ":"  ").Append(e.GetType().Name).Append(" '").Append(ZOwnText(e)).Append("' cls=").Append(ZCls(e)).Append(" tip='").Append(e.tooltip).Append("' wb=").Append(e.worldBound).Append("\n");
return sb.ToString();
