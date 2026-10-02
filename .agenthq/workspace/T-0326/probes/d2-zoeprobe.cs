var sb = new System.Text.StringBuilder();
var w = ZWin("ZoeWindow"); if (w == null) return "no window";
foreach (var e in ZAll(w.rootVisualElement))
{
    if (!ZDrawn(e)) continue;
    var b = e as UnityEngine.UIElements.Button;
    if (b != null && (b.text == "▶" || b.text.Contains("▶")))
        sb.Append("PLAY-BTN '").Append(b.text).Append("' enabled=").Append(b.enabledInHierarchy)
          .Append(" tip=").Append(ZTip(b)).Append("\n");
    var d = e as UnityEngine.UIElements.DropdownField;
    if (d != null)
        sb.Append("DROPDOWN value='").Append(d.value).Append("' choices=").Append(d.choices.Count)
          .Append(" enabled=").Append(d.enabledInHierarchy).Append(" caption=").Append(ZCaption(d)).Append("\n");
    var tf = e as UnityEngine.UIElements.TextField;
    if (tf != null)
        sb.Append("TEXTFIELD label='").Append(tf.label).Append("' value='").Append(tf.value)
          .Append("' enabled=").Append(tf.enabledInHierarchy).Append(" path=").Append(ZPath(tf)).Append("\n");
}
var rep = ZAudit(w, "ZoeWindow-fresh");
sb.Append(ZSummary("ZoeWindow-fresh"));
sb.Append("dump=").Append(ZDump("audit-ZoeWindow-fresh", rep)).Append("\n");
return sb.ToString();
