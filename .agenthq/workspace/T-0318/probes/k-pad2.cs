var win = ZWin("ShaperWindow"); if (win == null) return "NO SHAPER";
var BFa = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.MethodInfo rb = null;
for (var t = win.GetType(); t != null && rb == null; t = t.BaseType) rb = t.GetMethod("Rebuild", BFa|System.Reflection.BindingFlags.DeclaredOnly);
System.Func<string> padVal = () => {
    foreach (var e in ZAll(win.rootVisualElement))
        if (e.GetType().Name == "ZuiPad" && ZDrawn(e)) return ZCaption(e) + " Value=" + e.GetType().GetProperty("Value", BFa).GetValue(e);
    return "<no pad>";
};
var sb = new System.Text.StringBuilder();
sb.Append("now            ").Append(padVal()).Append("\n");
rb.Invoke(win, null); win.Repaint();
sb.Append("afterRebuild   ").Append(padVal()).Append("\n");
// does the window listen for undo at all?
var ev = typeof(UnityEditor.Undo).GetEvent("undoRedoPerformed");
int n = 0;
foreach (var f in win.GetType().GetFields(BFa)) if (f.Name.ToLower().Contains("undo")) n++;
bool hasHook = false;
foreach (var m in win.GetType().GetMethods(BFa)) if (m.Name.ToLower().Contains("undo")) { hasHook = true; sb.Append("method ").Append(m.Name).Append("\n"); }
sb.Append("undoFields=").Append(n).Append(" undoMethods=").Append(hasHook).Append("\n");
return sb.ToString();
