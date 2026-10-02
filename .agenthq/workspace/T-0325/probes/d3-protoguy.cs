var sb = new System.Text.StringBuilder();
sb.Append(ZBind("ZoeWindow", "Assets/Demos/ProtoGuyDemo/ProtoGuy.asset")).Append("\n");
var w = ZWin("ZoeWindow");
foreach (var e in ZAll(w.rootVisualElement))
{
    if (!ZDrawn(e)) continue;
    var b = e as UnityEngine.UIElements.Button;
    if (b != null && b.text.Contains("▶"))
        sb.Append("PLAY-BTN enabled=").Append(b.enabledInHierarchy).Append("\n");
    var d = e as UnityEngine.UIElements.DropdownField;
    if (d != null)
    {
        sb.Append("DROPDOWN value='").Append(d.value).Append("' enabled=").Append(d.enabledInHierarchy).Append(" choices=[");
        foreach (var c in d.choices) sb.Append(c).Append("|");
        sb.Append("]\n");
    }
}
return sb.ToString();
