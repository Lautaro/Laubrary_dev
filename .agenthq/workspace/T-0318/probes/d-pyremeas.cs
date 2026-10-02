var win = ZWin("PyreWindow"); if (win == null) return "NO PYRE";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
var sb = new System.Text.StringBuilder();
sb.Append("intent=").Append(win.GetType().GetField("leftPaneWidth", BFi).GetValue(win))
  .Append(" window=").Append(win.position.width.ToString("F1")).Append("\n");
var root = win.rootVisualElement;
foreach (var e in ZAll(root))
{
    if (!ZDrawn(e)) continue;
    var p = e.hierarchy.parent; if (p == null) continue;
    var pc = ZContentWorld(p);
    float sp = e.worldBound.xMax - pc.xMax;
    if (sp > ZTOL && e.worldBound.width >= 200f && pc.width > 200f)
        sb.Append("SPILL ").Append(sp.ToString("F1")).Append("px '").Append(ZCaption(e)).Append("' ")
          .Append(e.GetType().Name).Append(" x=").Append(e.worldBound.x.ToString("F1")).Append("..").Append(e.worldBound.xMax.ToString("F1"))
          .Append(" parentContent=").Append(pc.x.ToString("F1")).Append("..").Append(pc.xMax.ToString("F1")).Append("\n");
}
int off = 0; var rw = root.worldBound;
foreach (var e in ZAll(root)) { if (ZDrawn(e) && e.worldBound.xMax > rw.xMax + ZTOL) off++; }
sb.Append("rootWorld=").Append(rw.x.ToString("F1")).Append("..").Append(rw.xMax.ToString("F1")).Append(" drawnPastRoot=").Append(off).Append("\n");
return sb.ToString();
