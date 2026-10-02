// Type a value that FITS into the Size input, through the field's own value setter (the same
// ChangeEvent a typed edit raises), and see the input come back to its 46 px resting width.
var win = ZWin("PyreWindow"); if (win == null) return "no window";
foreach (var e in ZAll(win.rootVisualElement))
{
    if (e.GetType().Name != "ZuiMicroSlider" || !ZDrawn(e)) continue;
    UnityEngine.UIElements.Label cap = null;
    foreach (var c in ZAll(e)) { if (ZCls(c).Contains("zui-microslider__caption")) { cap = c as UnityEngine.UIElements.Label; break; } }
    if (cap == null || cap.text != "Size") continue;
    foreach (var c in ZAll(e))
    {
        var ff = c as UnityEngine.UIElements.FloatField;
        if (ff == null) continue;
        float was = ff.value;
        ff.value = 1.5f;
        return "Size " + was + " -> " + ff.value + " (field was " + ff.worldBound.width.ToString("F2") + " px)";
    }
}
return "not found";
