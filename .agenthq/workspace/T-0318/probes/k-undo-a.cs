var win = ZWin("ShaperWindow"); if (win == null) return "NO SHAPER";
var BFa = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
foreach (var e in ZAll(win.rootVisualElement))
    if (e.GetType().Name == "ZuiPad" && ZDrawn(e) && e.enabledInHierarchy)
    {
        e.Focus();
        string b = e.GetType().GetProperty("Value", BFa).GetValue(e).ToString();
        for (int k=0;k<2;k++) using (var ev = UnityEngine.UIElements.KeyDownEvent.GetPooled('\0', UnityEngine.KeyCode.RightArrow, UnityEngine.EventModifiers.None)) { ev.target = e; e.SendEvent(ev); }
        return "before=" + b + " after=" + e.GetType().GetProperty("Value", BFa).GetValue(e) + " group='" + UnityEditor.Undo.GetCurrentGroupName() + "'";
    }
return "no pad";
