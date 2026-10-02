// ZPress(win, el) — press an element the way a user can, and say honestly whether the press could land.
//
// Two things every earlier round's press helper got wrong, both measured this session:
//   1. an element can be INSIDE the window rect and still be scrolled under the window's own fixed
//      header (the toolbar + section bar sit outside the ScrollView and paint over it), so the press
//      lands on the header instead — `panel.Pick(centre)` is the only honest test of "can this be hit";
//   2. `ScrollTo` in eval N only takes effect for eval N+1, so a scroll must return and be re-run.
//
// Returns: "ok …" (pressed), "scrolled …" (rerun), or "BLOCKED …" (nothing can reach it).
System.Func<UnityEditor.EditorWindow, UnityEngine.UIElements.VisualElement, string> ZPress = (win, el) =>
{
    if (el == null) return "BLOCKED null element";
    var panel = win.rootVisualElement.panel;
    var c = el.worldBound.center;
    var hit = panel == null ? null : panel.Pick(c);
    bool reaches = false;
    for (var p = hit; p != null; p = p.hierarchy.parent) if (p == el) { reaches = true; break; }
    if (!reaches)
    {
        UnityEngine.UIElements.ScrollView sv = null;
        for (var p = el.hierarchy.parent; p != null; p = p.hierarchy.parent) { var s = p as UnityEngine.UIElements.ScrollView; if (s != null) { sv = s; break; } }
        if (sv == null)
            return "BLOCKED '" + ZCaption(el) + "' at " + el.worldBound + " is covered by "
                 + (hit == null ? "<nothing>" : hit.GetType().Name + " cls=" + ZCls(hit)) + " and has no ScrollView to scroll it out";
        sv.ScrollTo(el); win.Repaint();
        return "scrolled '" + ZCaption(el) + "' (was " + el.worldBound + ", covered by "
             + (hit == null ? "<nothing>" : ZCls(hit)) + ") offset=" + sv.scrollOffset + " — rerun";
    }
    ZClick(el);
    return "ok pressed '" + ZCaption(el) + "' at " + el.worldBound;
};
