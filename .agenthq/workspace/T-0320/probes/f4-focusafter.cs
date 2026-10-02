var win = ZWin("ShaperWindow"); if (win==null) return "no window";
var all = ZAll(win.rootVisualElement);
var sb = new System.Text.StringBuilder();
var f = win.rootVisualElement.panel != null ? win.rootVisualElement.panel.focusController.focusedElement : null;
sb.AppendLine("focused=" + (f==null?"null":f.GetType().Name));
foreach (var e in all) { var n = e.GetType().Name;
  if (n == "ZuiValue2DControl" && ZDrawn(e))
    sb.AppendLine(n + " " + e.worldBound + " bw=" + e.resolvedStyle.borderTopWidth + " bcol=" + e.resolvedStyle.borderTopColor + " focused=" + (object.ReferenceEquals(e, f)));
}
return sb.ToString();
