var sb=new System.Text.StringBuilder();
var win = ZWin("ShaperWindow");
sb.Append("pos=").Append(win.position).Append(" rootChildren=").Append(win.rootVisualElement.hierarchy.childCount).Append(" all=").Append(ZAll(win.rootVisualElement).Count).Append("\n");
int nb=0;
foreach (var e in ZAll(win.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b==null) continue; nb++; if (nb<40) sb.Append("[").Append(b.text).Append(" drawn=").Append(ZDrawn(b)).Append("] "); }
sb.Append("\nbuttons=").Append(nb);
return sb.ToString();
