// Turn every OFF segment of the focused window's section toggle bar ON, one press per call (each press
// rebuilds the window and stales every handle).
string wn = UnityEditor.EditorPrefs.GetString("T326.walkWin", "ShaperWindow");
var win = ZWin(wn); if (win == null) return "no window " + wn;
var sb = new System.Text.StringBuilder();
int pressed = 0, on = 0, off = 0;
foreach (var e in ZAll(win.rootVisualElement))
{
    var b = e as UnityEngine.UIElements.Button;
    if (b == null || !ZDrawn(b)) continue;
    if (!ZCls(b).Contains("zui-segmented__seg")) continue;
    if (b.text == "Sections" || b.text == "Toggle Bar") continue;
    // ONLY the section bar's own segments. Without this the sweep also pressed a light's Point/Directional
    // kind inside a section it had just opened — a DATA edit on the shipped demo document, not a view toggle.
    bool inBar = false;
    for (var p = b.hierarchy.parent; p != null; p = p.hierarchy.parent)
        if (p.GetType().Name == "ZuiSectionToggleBar") { inBar = true; break; }
    if (!inBar) continue;
    if (ZCls(b).Contains("zui-segmented__on")) { on++; continue; }
    off++;
    if (pressed == 0) { ZClick(b); pressed++; sb.Append("pressed '").Append(b.text).Append("'\n"); }
}
sb.Append("on=").Append(on).Append(" off=").Append(off).Append("\n");
return sb.ToString();
