// Press the toggle-bar's own "Weapons" entry, the way a user does.
var win = ZWin("ZoeWindow"); if (win == null) return "no window";
var sb = new System.Text.StringBuilder();
foreach (var e in ZAll(win.rootVisualElement))
{
    if (!ZDrawn(e)) continue;
    var b = e as UnityEngine.UIElements.Button;
    if (b == null) continue;
    if (b.text != "Weapons" && b.text != "Cues" && b.text != "AI") continue;
    sb.Append("pressing '").Append(b.text).Append("' cls=").Append(ZCls(b)).Append(" -> ").Append(ZClick(b)).Append("\n");
}
return sb.ToString();
