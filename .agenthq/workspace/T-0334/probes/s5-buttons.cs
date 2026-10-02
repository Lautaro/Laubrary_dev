var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var sb = new System.Text.StringBuilder();
var wr = w.rootVisualElement.worldBound;
sb.Append("root=").Append(wr).Append("\n");
foreach (var e in ZAll(w.rootVisualElement)) {
  var b = e as UnityEngine.UIElements.Button; if (b == null || !ZDrawn(b)) continue;
  if (string.IsNullOrEmpty(b.text)) continue;
  bool off = b.worldBound.yMax > wr.yMax + 1.5f || b.worldBound.yMin < wr.yMin - 1.5f;
  sb.Append(off ? "OFF " : "    ").Append("'").Append(b.text).Append("' en=").Append(b.enabledInHierarchy)
    .Append(" y=").Append(b.worldBound.y.ToString("F0")).Append(" w=").Append(b.worldBound.width.ToString("F0")).Append("\n");
}
foreach (var e in ZAll(w.rootVisualElement)) {
  var t = e as UnityEngine.UIElements.Toggle; if (t == null || !ZDrawn(t)) continue;
  sb.Append("TOGGLE '").Append(ZCaption(t)).Append("' val=").Append(t.value).Append(" cls=").Append(ZCls(t)).Append("\n");
}
return sb.ToString();
