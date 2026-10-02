// Every chip in the section toggle bar: is it on, enabled, and does it name a section that EXISTS?
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var sb = new System.Text.StringBuilder();
foreach (var e in ZAll(w.rootVisualElement)) {
  var b = e as UnityEngine.UIElements.Button; if (b == null || !ZDrawn(b)) continue;
  string bar = null; for (var p = b.hierarchy.parent; p != null; p = p.hierarchy.parent) if (p.GetType().Name == "ZuiSectionToggleBar") { bar = ZCls(p); break; }
  if (bar == null) continue;
  sb.Append("'").Append(b.text).Append("' on=").Append(ZCls(b).Contains("zui-segmented__on"))
    .Append(" en=").Append(b.enabledInHierarchy)
    .Append(" cls=").Append(ZCls(b))
    .Append(" tip=").Append(ZTip(b)).Append("\n");
}
var secT = ZType("ZuiSection");
sb.Append("-- sections in the tree --\n");
foreach (var e in ZAll(w.rootVisualElement)) { if (!secT.IsInstanceOfType(e)) continue;
  string title=""; foreach (var c in ZAll(e)) { var l=c as UnityEngine.UIElements.Label; if (l!=null&&l.ClassListContains("zui-section__title")) { title=l.text; break; } }
  sb.Append("  '").Append(title).Append("' drawn=").Append(ZDrawn(e)).Append("\n"); }
sb.Append("userSel=").Append(UnityEditor.EditorPrefs.GetString("ZuiSectionToggleBar.ShaperWindow.userSel","<unset>")).Append("\n");
return sb.ToString();
