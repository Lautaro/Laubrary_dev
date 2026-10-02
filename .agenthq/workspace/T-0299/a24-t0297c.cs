var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);

UnityEngine.UIElements.VisualElement box = null, radio = null, field = null;
foreach (var v in all)
{
    if (v.ClassListContains("zui-radio--wrap") && v.tooltip == "probe") radio = v;
    if (v.ClassListContains("zui-field--wrap") && v.tooltip == "probe") field = v;
}
foreach (var v in all) if (v.GetType().Name == "ZuiBox" && v.resolvedStyle.width > 340f && v.resolvedStyle.width < 360f) box = v;
sb.Append("radio=").Append(radio != null).Append(" field=").Append(field != null).Append(" box=").Append(box != null).Append("\n");
if (radio != null)
{
    sb.Append("radio  w=").Append(radio.resolvedStyle.width.ToString("F1")).Append(" h=").Append(radio.resolvedStyle.height.ToString("F1"))
      .Append(" world=").Append(radio.worldBound.ToString()).Append("\n");
    sb.Append("wrapped onto more than one line = ").Append(radio.resolvedStyle.height > 26f).Append("\n");
}
if (field != null) sb.Append("field  w=").Append(field.resolvedStyle.width.ToString("F1")).Append(" flexShrink=").Append(field.resolvedStyle.flexShrink).Append("\n");
if (box != null) sb.Append("box    w=").Append(box.resolvedStyle.width.ToString("F1")).Append(" xMax=").Append(box.worldBound.xMax.ToString("F1")).Append("\n");
if (radio != null && box != null)
    sb.Append("radio xMax=").Append(radio.worldBound.xMax.ToString("F1")).Append(" vs box xMax=").Append(box.worldBound.xMax.ToString("F1"))
      .Append("  OVERFLOW=").Append((radio.worldBound.xMax - box.worldBound.xMax).ToString("F1")).Append("px\n");
// every option button legible (inside the box)?
if (radio != null)
{
    var kids = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(radio, kids);
    foreach (var k in kids) if (k is UnityEngine.UIElements.Button b)
        sb.Append("   option '").Append(b.text).Append("' x=").Append(b.worldBound.xMin.ToString("F1")).Append("..").Append(b.worldBound.xMax.ToString("F1"))
          .Append(" y=").Append(b.worldBound.yMin.ToString("F1")).Append(" inside=").Append(box != null && b.worldBound.xMax <= box.worldBound.xMax + 0.5f).Append("\n");
}
return sb.ToString();
