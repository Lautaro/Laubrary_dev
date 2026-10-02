var win = ZWin("ZoeWindow"); if (win == null) return "no window";
var sb = new System.Text.StringBuilder();
foreach (var e in ZAll(win.rootVisualElement))
{
    var d = e as UnityEngine.UIElements.DropdownField;
    if (d == null || !ZDrawn(d)) continue;
    if (!d.value.Contains("unresolved") && d.choices.Count > 2) continue;
    sb.Append("DROPDOWN value='").Append(d.value).Append("' choices=").Append(d.choices.Count)
      .Append(" enabled=").Append(d.enabledInHierarchy).Append("\n   near=").Append(ZCaption(d.hierarchy.parent))
      .Append("\n   tip=").Append(ZTip(d)).Append("\n   path=").Append(ZPath(d)).Append("\n");
}
return sb.ToString();
