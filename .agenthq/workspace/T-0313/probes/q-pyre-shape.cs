// T-0313 — what does Pyre have to draw?  Enumerates every concrete PyreForm subclass and every ShapeForm
// enum value, and reports the Pyre window's dial-pane chain (widths, flex, min/max) so the column-count
// question can be answered from geometry rather than from the T-0312 README's claim that Pyre is 1-column.
var sb = new System.Text.StringBuilder();
var formT = ZType("PyreForm");
sb.Append("PyreForm=").Append(formT == null ? "<null>" : formT.FullName).Append("\n");
int n = 0;
if (formT != null)
    foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
    {
        System.Type[] ts; try { ts = a.GetTypes(); } catch { continue; }
        foreach (var t in ts)
            if (!t.IsAbstract && formT.IsAssignableFrom(t) && t != formT)
            { n++; sb.Append("FORM ").Append(t.Name).Append(" | ").Append(t.FullName).Append("\n"); }
    }
sb.Append("formCount=").Append(n).Append("\n");
var shT = ZType("ShapeForm");
if (shT != null && shT.IsEnum)
{
    sb.Append("SHAPES ");
    foreach (var v in System.Enum.GetNames(shT)) sb.Append(v).Append(" ");
    sb.Append("\n");
}

var win = ZWin("PyreWindow");
if (win != null)
{
    UnityEngine.UIElements.VisualElement flow = null;
    foreach (var v in ZAll(win.rootVisualElement)) if (v.GetType().Name == "ZuiColumnFlow") { flow = v; break; }
    sb.Append("window=").Append(win.position).Append("\n");
    if (flow == null) sb.Append("NO ZuiColumnFlow in PyreWindow\n");
    else
    {
        sb.Append("flow world=").Append(flow.worldBound).Append(" columns=")
          .Append(flow.hierarchy.childCount > 0 ? flow.hierarchy[0].hierarchy.childCount : 0).Append("\n");
        int depth = 0;
        for (var p = flow; p != null && depth < 8; p = p.hierarchy.parent, depth++)
            sb.Append("  chain[").Append(depth).Append("] ").Append(p.GetType().Name).Append("#").Append(p.name)
              .Append(" w=").Append(p.worldBound.width.ToString("F1"))
              .Append(" styleW=").Append(p.resolvedStyle.width.ToString("F1"))
              .Append(" grow=").Append(p.resolvedStyle.flexGrow).Append(" shrink=").Append(p.resolvedStyle.flexShrink)
              .Append(" min=").Append(p.resolvedStyle.minWidth.value.ToString("F0"))
              .Append(" max=").Append(p.resolvedStyle.maxWidth.value.ToString("F0"))
              .Append("\n");
    }
    var f = win.GetType().GetField("leftPaneWidth", System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
    sb.Append("leftPaneWidth field=").Append(f == null ? "<none>" : f.GetValue(win).ToString()).Append("\n");
}
else sb.Append("Pyre window not open\n");
return sb.ToString();
