var win = ZWin("ShaperWindow"); if (win == null) return "NO SHAPER";
var BFa = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
var doc = UnityEditor.AssetDatabase.LoadMainAssetAtPath("Assets/Shaper/AuditT0318/NudgeDoc.asset");
foreach (var e in ZAll(win.rootVisualElement))
    if (e.GetType().Name == "ZuiPad" && ZDrawn(e))
        return "control now = " + e.GetType().GetProperty("Value", BFa).GetValue(e) + "  (no Rebuild called)";
return "no pad drawn";
