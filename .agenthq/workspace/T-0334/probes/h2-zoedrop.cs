var win = ZWin("ZoeWindow"); if (win == null) return "no window";
var sb = new System.Text.StringBuilder();
int tf = 0;
foreach (var e in ZAll(win.rootVisualElement))
{
    if (!ZDrawn(e)) continue;
    var t = e as UnityEngine.UIElements.TextField;
    if (t != null) { tf++; sb.Append("TEXTFIELD caption='").Append(ZCaption(t)).Append("' value='").Append(t.value).Append("'\n"); }
    var d = e as UnityEngine.UIElements.DropdownField;
    if (d != null)
    {
        sb.Append("DROPDOWN caption='").Append(ZCaption(d)).Append("' value='").Append(d.value)
          .Append("' enabled=").Append(d.enabledInHierarchy).Append(" choices=[");
        foreach (var c in d.choices) sb.Append(c).Append("|");
        sb.Append("]\n");
    }
}
sb.Append("textInputs=").Append(tf).Append("\n");
return sb.ToString();
