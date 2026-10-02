var sb = new System.Text.StringBuilder();
var w = ZWin("ZoeWindow");
foreach (var e in ZAll(w.rootVisualElement))
{
    if (!ZDrawn(e)) continue;
    var d = e as UnityEngine.UIElements.DropdownField;
    if (d != null)
    {
        sb.Append("DROPDOWN label='").Append(d.label).Append("' value='").Append(d.value)
          .Append("' choices=").Append(d.choices.Count).Append(" enabled=").Append(d.enabledInHierarchy)
          .Append(" [").Append(string.Join(" | ", d.choices.ToArray())).Append("]\n");
    }
    var tf = e as UnityEngine.UIElements.TextField;
    if (tf != null) sb.Append("TEXTFIELD label='").Append(tf.label).Append("' caption='").Append(ZCaption(tf)).Append("'\n");
}
return sb.ToString();
