var win = ZWin("ZoeWindow"); if (win == null) return "no window";
var sb = new System.Text.StringBuilder();
foreach (var e in ZAll(win.rootVisualElement))
{
    var b = e as UnityEngine.UIElements.Button;
    if (b == null || !ZDrawn(b) || b.text != "Weapons") continue;
    bool on = ZCls(b).Contains("zui-segmented__on");
    sb.Append("Weapons was ").Append(on ? "ON" : "OFF");
    if (!on) { ZClick(b); sb.Append(" -> pressed"); }
    sb.Append("\n");
}
return sb.ToString();
