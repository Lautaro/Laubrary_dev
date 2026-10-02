// T-0318 — T-0316 live: does an arrow key on a focused ZuiMicroMinMax / ZuiPad / ZuiValue2DControl
// actually change the value, and is that change ONE undo step that restores it?  Reads the underlying
// value through the control's own public surface, dispatches a real KeyDownEvent, re-reads, Undo, re-reads.
var win = ZWin("ShaperWindow"); if (win == null) return "NO SHAPER";
var sb = new System.Text.StringBuilder();
var BFa = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;

System.Func<UnityEngine.UIElements.VisualElement, string> read = e =>
{
    var t = e.GetType();
    var s = new System.Text.StringBuilder();
    foreach (var n in new[]{ "lowValue","highValue","minValue","maxValue","value","Value","Low","High" })
    {
        var p = t.GetProperty(n, BFa); if (p != null && p.CanRead) { object v = null; try { v = p.GetValue(e); } catch {} if (v != null) s.Append(n).Append('=').Append(v).Append(' '); }
        var f = t.GetField(n, BFa); if (f != null) { object v = null; try { v = f.GetValue(e); } catch {} if (v != null) s.Append(n).Append('#').Append(v).Append(' '); }
    }
    return s.ToString();
};

foreach (var typeName in new[]{ "ZuiMicroMinMax", "ZuiPad", "ZuiValue2DControl" })
{
    UnityEngine.UIElements.VisualElement ctl = null;
    foreach (var e in ZAll(win.rootVisualElement))
        if (e.GetType().Name == typeName && ZDrawn(e) && e.enabledInHierarchy) { ctl = e; break; }
    if (ctl == null) { sb.Append(typeName).Append(": not drawn in this state\n"); continue; }
    sb.Append("== ").Append(typeName).Append(" '").Append(ZCaption(ctl)).Append("'\n");
    sb.Append("   focusable=").Append(ctl.focusable).Append(" tabIndex=").Append(ctl.tabIndex).Append("\n");
    ctl.Focus();
    var fc = win.rootVisualElement.focusController;
    sb.Append("   focused=").Append(fc.focusedElement == ctl).Append("\n");
    string before = read(ctl);
    int undoBefore = 0;
    for (int k = 0; k < 3; k++)
        using (var ev = UnityEngine.UIElements.KeyDownEvent.GetPooled('\0', UnityEngine.KeyCode.RightArrow, UnityEngine.EventModifiers.None))
        { ev.target = ctl; ctl.SendEvent(ev); }
    string after = read(ctl);
    sb.Append("   before ").Append(before).Append("\n   after  ").Append(after).Append("\n");
    sb.Append("   CHANGED=").Append(before != after).Append("\n");
    string uname = UnityEditor.Undo.GetCurrentGroupName();
    sb.Append("   undoGroupName='").Append(uname).Append("'\n");
    for (int k = 0; k < 3; k++) UnityEditor.Undo.PerformUndo();
    string undone = read(ctl);
    sb.Append("   afterUndo ").Append(undone).Append("\n   RESTORED=").Append(undone == before).Append("\n");
    undoBefore++;
}
return sb.ToString();
