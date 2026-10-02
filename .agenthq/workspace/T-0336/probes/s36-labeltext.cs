var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var sb=new System.Text.StringBuilder();
foreach (var e in ZAll(w.rootVisualElement)) {
  var f = e as UnityEditor.UIElements.ObjectField; if (f==null||!ZDrawn(f)) continue;
  foreach (var d in ZAll(f)) if (d.ClassListContains("unity-object-field-display__label")) {
    var l = d as UnityEngine.UIElements.Label;
    sb.Append("label.text='").Append(l.text).Append("'\n  overflow=").Append(l.resolvedStyle.textOverflow)
      .Append(" whiteSpace=").Append(l.resolvedStyle.whiteSpace)
      .Append(" rect=").Append(l.worldBound).Append("\n  fieldTip=").Append(ZTip(f).Length>90?ZTip(f).Substring(0,90):ZTip(f)).Append("\n");
  }
}
return sb.ToString();
