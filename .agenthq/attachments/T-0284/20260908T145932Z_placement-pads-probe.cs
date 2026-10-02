// A19 — the four placement pads: are they collapsed thumbnails, and does a real left PointerDown expand one?
var SB = new System.Text.StringBuilder();
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> WalkT = null;
WalkT = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) WalkT(e[i], into); };
System.Func<System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Tree = () =>
{ var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); WalkT(win.rootVisualElement, l); return l; };
System.Func<UnityEngine.UIElements.VisualElement, string> TextOf = v =>
{ var p = v.GetType().GetProperty("text", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
  if (p != null && p.PropertyType == typeof(string)) { try { return (string)p.GetValue(v); } catch { } } return null; };

string[] wanted = { "Translate", "Origin", "Scale", "Skew" };
foreach (var w in wanted)
{
    UnityEngine.UIElements.VisualElement pad = null, lab = null;
    foreach (var v in Tree())
    {
        if (v.GetType().Name != "ZuiValue2DControl") continue;
        for (int i = 0; i < v.childCount && lab == null; i++)
            for (int j = 0; j < v[i].childCount; j++)
                if (TextOf(v[i][j]) == w) { pad = v; lab = v[i][j]; break; }
        if (pad != null) break;
    }
    if (pad == null) { SB.Append(w).Append(": NOT FOUND\n"); continue; }
    var all0 = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); WalkT(pad, all0);
    var rBefore = pad.worldBound;
    SB.Append(w).Append(": elements before=").Append(all0.Count)
      .Append(" size=").Append((int)rBefore.width).Append('x').Append((int)rBefore.height);
    // send a genuine left PointerDownEvent at the label's own centre
    var e2 = UnityEngine.UIElements.PointerDownEvent.GetPooled();
    e2.target = lab;
    var bf = typeof(UnityEngine.UIElements.PointerEventBase<UnityEngine.UIElements.PointerDownEvent>).GetProperty("button");
    if (bf != null && bf.CanWrite) bf.SetValue(e2, 0);
    lab.SendEvent(e2);
    var all1 = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); WalkT(pad, all1);
    SB.Append("  -> after=").Append(all1.Count).Append(all1.Count > all0.Count ? "  EXPANDED" : "  UNCHANGED").Append('\n');
}
win.Repaint();
return SB.ToString();
