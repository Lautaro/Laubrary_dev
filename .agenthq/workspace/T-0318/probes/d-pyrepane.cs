// T-0318 — Pyre's right pane vs the window at a given leftPaneWidth intent and window width.
// Prefs: T318.lpw (the persisted intent to write), T318.winw.
var win = ZWin("PyreWindow"); if (win == null) return "NO PYRE";
float lpw = float.Parse(UnityEditor.EditorPrefs.GetString("T318.lpw","360"));
float ww  = float.Parse(UnityEditor.EditorPrefs.GetString("T318.winw","820"));
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
var f = win.GetType().GetField("leftPaneWidth", BFi); f.SetValue(win, lpw);
win.position = new UnityEngine.Rect(5, 20, ww, 900);
System.Reflection.MethodInfo rb = null;
for (var t = win.GetType(); t != null && rb == null; t = t.BaseType) rb = t.GetMethod("Rebuild", BFi|System.Reflection.BindingFlags.DeclaredOnly);
if (rb != null) rb.Invoke(win, null);
win.Repaint();
var sb = new System.Text.StringBuilder();
sb.Append("intent=").Append(lpw).Append(" window=").Append(win.position.width.ToString("F1")).Append("\n");
var root = win.rootVisualElement;
sb.Append("rootContent=").Append(ZContentWorld(root).ToString()).Append("\n");
foreach (var e in ZAll(root))
{
    var n = e.name ?? "";
    if (n.Contains("rightPane") || n.Contains("leftPane") || n.Contains("Pane"))
        sb.Append("PANE ").Append(e.GetType().Name).Append('#').Append(n).Append(" ").Append(e.worldBound.ToString()).Append("\n");
}
foreach (var e in ZAll(root))
{
    if (!ZDrawn(e)) continue;
    var p = e.hierarchy.parent; if (p == null) continue;
    var pc = ZContentWorld(p);
    float sp = e.worldBound.xMax - pc.xMax;
    if (sp > ZTOL && e.worldBound.width > 100f)
        sb.Append("SPILL ").Append(sp.ToString("F1")).Append("px '").Append(ZCaption(e)).Append("' ")
          .Append(e.GetType().Name).Append(" ").Append(e.worldBound.ToString()).Append(" parentContent=").Append(pc.ToString()).Append("\n");
}
return sb.ToString();
