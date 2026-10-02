// T-0313 — read the Destination row after a rebuild (a later eval, so the layout has settled).
var win = ZWin("ShaperWindow");
if (win == null) return "NO SHAPER WINDOW";
var sb = new System.Text.StringBuilder();
var root = win.rootVisualElement;
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath)
  .Append(" window=").Append(win.position.width.ToString("F0")).Append("\n");

// the right pane = the Split's second child; report its width, since the Bake box lives in it
foreach (var e in ZAll(root))
{
    if (!ZDrawn(e)) continue;
    if (e.GetType().Name.IndexOf("Split", System.StringComparison.Ordinal) < 0) continue;
    for (int i = 0; i < e.hierarchy.childCount; i++)
        sb.Append("split child[").Append(i).Append("] ").Append(e.hierarchy[i].GetType().Name)
          .Append(" w=").Append(e.hierarchy[i].worldBound.width.ToString("F1")).Append("\n");
    break;
}

int found = 0;
foreach (var e in ZAll(root))
{
    if (!ZDrawn(e) || !e.ClassListContains("zui-field")) continue;
    if (ZCaption(e) != "Destination") continue;
    found++;
    var pc = ZContentWorld(e.hierarchy.parent);
    sb.Append("FIELD 'Destination' w=").Append(e.worldBound.width.ToString("F1"))
      .Append(" x=").Append(e.worldBound.xMin.ToString("F1")).Append("..").Append(e.worldBound.xMax.ToString("F1"))
      .Append("  parentContent=").Append(pc.width.ToString("F1"))
      .Append(" (").Append(pc.xMin.ToString("F1")).Append("..").Append(pc.xMax.ToString("F1")).Append(")")
      .Append(e.worldBound.xMax > pc.xMax + ZTOL ? "  << OVERFLOWS by " + (e.worldBound.xMax - pc.xMax).ToString("F1") : "")
      .Append("\n");
    foreach (var c in ZAll(e))
    {
        var te = c as UnityEngine.UIElements.TextElement;
        if (te == null || string.IsNullOrEmpty(te.text)) continue;
        float need = ZNeed(te), have = ZHave(te);
        sb.Append("   text '").Append(te.text).Append("' (").Append(te.text.Length).Append(" chars)")
          .Append(" needs=").Append(need.ToString("F1")).Append(" has=").Append(have.ToString("F1"))
          .Append(" whiteSpace=").Append(te.resolvedStyle.whiteSpace)
          .Append(" overflowClip=").Append(te.resolvedStyle.textOverflow)
          .Append(need > have + ZTOL ? "   << CUT by " + (need - have).ToString("F1") + "px" : "   fits")
          .Append("\n      own tip=[").Append(te.tooltip).Append("] effective=[").Append(ZTip(te))
          .Append("] type=").Append(te.GetType().Name).Append(" cls=").Append(ZCls(te)).Append("\n");
    }
}
sb.Append("destination fields found=").Append(found).Append("\n");
var path = ZDump("bakedest-" + UnityEditor.EditorPrefs.GetString("T0312.tag", "x") + ".txt", sb.ToString());
return path + "\n" + sb.ToString();
