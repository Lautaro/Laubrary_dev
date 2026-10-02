var win = ZWin("ShaperWindow"); var sb = new System.Text.StringBuilder();
sb.Append("pos=").Append(win.position).Append("\n");
foreach (var e in ZAll(win.rootVisualElement)) {
  var b = e as UnityEngine.UIElements.Button; if (b == null || !ZDrawn(b)) continue;
  string lbl = null; foreach (var c in ZAll(b)) { var l = c as UnityEngine.UIElements.Label; if (l != null && !string.IsNullOrEmpty(l.text)) { lbl = l.text; break; } }
  sb.Append("BTN '").Append(b.text).Append("'/'").Append(lbl).Append("' ").Append(b.worldBound).Append(" cls=").Append(ZCls(b)).Append("\n");
}
return sb.ToString();
