// T-0318 — arrow-key nudge on the three T-0316 controls, read through their real accessors
// (ZuiMicroMinMax.low/high, ZuiPad.Value, ZuiValue2DControl's source.Static), with an Undo
// afterwards and a window Rebuild so the re-read reflects the restored document, not stale UI.
var win = ZWin("ShaperWindow"); if (win == null) return "NO SHAPER";
var sb = new System.Text.StringBuilder();
var BFa = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
System.Reflection.MethodInfo rb = null;
for (var t = win.GetType(); t != null && rb == null; t = t.BaseType) rb = t.GetMethod("Rebuild", BFa|System.Reflection.BindingFlags.DeclaredOnly);

System.Func<UnityEngine.UIElements.VisualElement, string> read = e =>
{
    var t = e.GetType();
    if (t.Name == "ZuiMicroMinMax")
        return "low=" + t.GetProperty("low", BFa).GetValue(e) + " high=" + t.GetProperty("high", BFa).GetValue(e);
    if (t.Name == "ZuiPad") return "Value=" + t.GetProperty("Value", BFa).GetValue(e);
    // ZuiValue2DControl: read the source it drives
    foreach (var f in t.GetFields(BFa))
    {
        var v = f.GetValue(e); if (v == null) continue;
        var st = v.GetType(); if (!st.Name.Contains("Source")) continue;
        var p = st.GetProperty("Static", BFa); if (p == null) continue;
        return f.Name + ".Static=" + p.GetValue(v);
    }
    return "<no accessor>";
};
System.Func<string, UnityEngine.UIElements.VisualElement> find = tn =>
{
    foreach (var e in ZAll(win.rootVisualElement))
        if (e.GetType().Name == tn && ZDrawn(e) && e.enabledInHierarchy) return e;
    return null;
};

foreach (var typeName in new[]{ "ZuiMicroMinMax", "ZuiPad", "ZuiValue2DControl" })
{
    var ctl = find(typeName);
    if (ctl == null) { sb.Append(typeName).Append(": not drawn\n"); continue; }
    string cap = ZCaption(ctl);
    sb.Append("== ").Append(typeName).Append(" '").Append(cap).Append("'  focusable=").Append(ctl.focusable).Append("\n");
    ctl.Focus();
    sb.Append("   focused=").Append(win.rootVisualElement.focusController.focusedElement == ctl).Append("\n");
    string before = read(ctl);
    int g = UnityEditor.Undo.GetCurrentGroup();
    for (int k = 0; k < 3; k++)
        using (var ev = UnityEngine.UIElements.KeyDownEvent.GetPooled('\0', UnityEngine.KeyCode.RightArrow, UnityEngine.EventModifiers.None))
        { ev.target = ctl; ctl.SendEvent(ev); }
    string after = read(ctl);
    sb.Append("   before ").Append(before).Append("\n   after  ").Append(after)
      .Append("\n   CHANGED=").Append(before != after)
      .Append(" undoGroup='").Append(UnityEditor.Undo.GetCurrentGroupName()).Append("'")
      .Append(" groups=").Append(UnityEditor.Undo.GetCurrentGroup() - g).Append("\n");
    UnityEditor.Undo.RevertAllDownToGroup(g);
    if (rb != null) rb.Invoke(win, null);
    win.Repaint();
    var again = find(typeName);
    string undone = again == null ? "<gone>" : read(again);
    sb.Append("   afterUndo ").Append(undone).Append("   RESTORED=").Append(undone == before).Append("\n");
}
return sb.ToString();
