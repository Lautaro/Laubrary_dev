var win = ZWin("PyreWindow"); if (win == null) return "no window";
var sb = new System.Text.StringBuilder();
foreach (var e in ZAll(win.rootVisualElement))
{
    if (!ZDrawn(e)) continue;
    var te = e as UnityEngine.UIElements.TextElement;
    if (te == null || te.text != "Centre") continue;
    sb.Append("LABEL 'Centre' rect=").Append(te.worldBound).Append(" cls=").Append(ZCls(te)).Append("\n path=").Append(ZPath(te)).Append("\n");
    var par = te.hierarchy.parent;
    sb.Append("parent=").Append(par.GetType().Name).Append(" cls=").Append(ZCls(par)).Append(" tip=").Append(ZTip(par)).Append("\n");
    foreach (var sib in ZAll(par))
    {
        if (sib == par) continue;
        sb.Append("   ").Append(sib.GetType().Name).Append(" cls=").Append(ZCls(sib)).Append(" rect=").Append(sib.worldBound).Append("\n");
    }
}
return sb.ToString();
