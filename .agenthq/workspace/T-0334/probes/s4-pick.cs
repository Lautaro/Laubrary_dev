// One step of the shape-family walk, through the picker's OWN menu.
// T334.pick = entry label, T334.col = the column header it must come from ("" = any).
var win = ZWin("ShaperWindow"); if (win == null) return "no shaper";
var sb = new System.Text.StringBuilder();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;

// the Shape section's picker button (the first Button in it that carries children — the shape chip)
var secT = ZType("ZuiSection");
UnityEngine.UIElements.VisualElement shapeSec = null;
foreach (var e in ZAll(win.rootVisualElement))
{
    if (!secT.IsInstanceOfType(e) || !ZDrawn(e)) continue;
    foreach (var c in ZAll(e)) { var l = c as UnityEngine.UIElements.Label;
        if (l != null && l.ClassListContains("zui-section__title") && l.text.StartsWith("Shape")) { shapeSec = e; break; } }
    if (shapeSec != null) break;
}
if (shapeSec == null) return "no Shape section";
UnityEngine.UIElements.Button pick = null;
foreach (var e in ZAll(shapeSec)) { var b = e as UnityEngine.UIElements.Button; if (b != null && ZDrawn(b) && b.hierarchy.childCount > 0) { pick = b; break; } }
if (pick == null) return "no picker button";
sb.Append("picker '").Append(ZCaption(pick)).Append("' ").Append(pick.worldBound).Append("\n");
// a synthesized press only reaches a control that is actually on screen (round 11 §0, round 18 §4)
var wr = win.rootVisualElement.worldBound;
if (pick.worldBound.yMax > wr.yMax || pick.worldBound.yMin < wr.yMin)
{
    UnityEngine.UIElements.ScrollView sv = null;
    for (var p = pick.hierarchy.parent; p != null; p = p.hierarchy.parent) { var s = p as UnityEngine.UIElements.ScrollView; if (s != null) { sv = s; break; } }
    if (sv == null) return sb.Append("picker is off-window and has NO ScrollView ancestor — unreachable").ToString();
    sv.ScrollTo(pick); win.Repaint();
    return sb.Append("picker was off-window; scrolled (offset=").Append(sv.scrollOffset).Append(") — rerun").ToString();
}
ZClick(pick);

// the menu lives on the PANEL tree, not under rootVisualElement (round 12 §0)
var panelRoot = win.rootVisualElement.panel == null ? null : win.rootVisualElement.panel.visualTree;
if (panelRoot == null) return sb.Append("no panel").ToString();
var items = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
foreach (var e in ZAll(panelRoot)) if (e.ClassListContains("zui-menu__item")) items.Add(e);
sb.Append("menu items=").Append(items.Count).Append("\n");
if (items.Count == 0) return sb.ToString();

string want = UnityEditor.EditorPrefs.GetString("T334.pick", "");
string wantCol = UnityEditor.EditorPrefs.GetString("T334.col", "");
if (want.Length == 0)
{
    // enumerate: column → entries
    foreach (var it in items)
    {
        string label = null;
        foreach (var c in ZAll(it)) { var l = c as UnityEngine.UIElements.Label; if (l != null && l.text != "✓" && !string.IsNullOrEmpty(l.text)) { label = l.text; break; } }
        string col = "";
        for (var p = it.hierarchy.parent; p != null; p = p.hierarchy.parent)
        {
            foreach (var c in p.hierarchy.Children()) { var l = c as UnityEngine.UIElements.Label; if (l != null && l.ClassListContains("zui-menu__section")) { col = l.text; break; } }
            if (col.Length > 0) break;
        }
        sb.Append("  [").Append(col).Append("] ").Append(label).Append("\n");
    }
    return sb.ToString();
}
UnityEngine.UIElements.VisualElement target = null; string seen = "";
foreach (var it in items)
{
    string label = null;
    foreach (var c in ZAll(it)) { var l = c as UnityEngine.UIElements.Label; if (l != null && l.text != "✓" && !string.IsNullOrEmpty(l.text)) { label = l.text; break; } }
    if (label != want) continue;
    string col = "";
    for (var p = it.hierarchy.parent; p != null; p = p.hierarchy.parent)
    {
        foreach (var c in p.hierarchy.Children()) { var l = c as UnityEngine.UIElements.Label; if (l != null && l.ClassListContains("zui-menu__section")) { col = l.text; break; } }
        if (col.Length > 0) break;
    }
    seen += col + "|";
    if (wantCol.Length == 0 || col == wantCol) { target = it; break; }
}
if (target == null) return sb.Append("entry '").Append(want).Append("' not found (cols seen: ").Append(seen).Append(")").ToString();
ZClick(target);
sb.Append("picked '").Append(want).Append("' from '").Append(wantCol).Append("'\n");
return sb.ToString();
