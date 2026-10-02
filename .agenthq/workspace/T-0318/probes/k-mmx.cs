var win = ZWin("ShaperWindow"); if (win == null) return "NO SHAPER";
var BFa = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
UnityEngine.UIElements.VisualElement ctl = null;
foreach (var e in ZAll(win.rootVisualElement))
    if (e.GetType().Name == "ZuiMicroMinMax" && ZDrawn(e) && e.enabledInHierarchy) { ctl = e; break; }
if (ctl == null) return "no MicroMinMax drawn";
var t = ctl.GetType();
var sb = new System.Text.StringBuilder();
System.Func<string> snap = () => {
    var s = new System.Text.StringBuilder();
    foreach (var n in new[]{"_low","_high","_min","_max","_lastHandle"})
    { var f = t.GetField(n, BFa); if (f!=null) s.Append(n).Append('=').Append(f.GetValue(ctl)).Append(' '); }
    return s.ToString();
};
sb.Append("caption='").Append(ZCaption(ctl)).Append("'\n  before ").Append(snap()).Append("\n");
ctl.Focus();
sb.Append("  focused=").Append(win.rootVisualElement.focusController.focusedElement == ctl).Append("\n");
using (var ev = UnityEngine.UIElements.KeyDownEvent.GetPooled('\0', UnityEngine.KeyCode.RightArrow, UnityEngine.EventModifiers.None))
{ ev.target = ctl; ctl.SendEvent(ev); }
sb.Append("  after1 ").Append(snap()).Append("\n");
using (var ev = UnityEngine.UIElements.KeyDownEvent.GetPooled('\0', UnityEngine.KeyCode.RightArrow, UnityEngine.EventModifiers.None))
{ ev.target = ctl; win.rootVisualElement.panel.visualTree.SendEvent(ev); }
sb.Append("  after2 ").Append(snap()).Append("\n");
return sb.ToString();
