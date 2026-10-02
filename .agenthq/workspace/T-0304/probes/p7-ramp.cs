// Reads the Colour ramp field (Shape > … > box 7) and reports the stamp inputs.
System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
var pyreT = FT("PyreWindow");
UnityEditor.EditorWindow win = null;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w0 != null && w0.GetType() == pyreT) win = w0;
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.hierarchy.childCount; i++) Walk(e.hierarchy[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
var sb = new System.Text.StringBuilder();
int n = 0;
foreach (var v in all)
{
    if (!v.ClassListContains("zui-field")) continue;
    if (v.hierarchy.childCount < 2) continue;
    var lb = v.hierarchy[0] as UnityEngine.UIElements.Label;
    if (lb == null) continue;
    var ctrl = v.hierarchy[1];
    float ch = ctrl.resolvedStyle.height, lh = lb.resolvedStyle.height;
    float need = UnityEngine.Mathf.Max(ch + ctrl.resolvedStyle.marginTop + ctrl.resolvedStyle.marginBottom, lh + lb.resolvedStyle.marginTop + lb.resolvedStyle.marginBottom) + v.resolvedStyle.paddingTop + v.resolvedStyle.paddingBottom;
    var cur = v.style.height;
    bool stamped = cur.keyword == UnityEngine.UIElements.StyleKeyword.Undefined;
    float diff = stamped ? UnityEngine.Mathf.Abs(cur.value.value - need) : -1f;
    if (!stamped || diff > 0.001f)
    {
        n++;
        if (n <= 12)
            sb.Append("UNSETTLED '").Append(lb.text).Append("' ctrl=").Append(ctrl.GetType().Name)
              .Append(" wrapStyleH=").Append(stamped ? cur.value.value.ToString("F3") : "unset")
              .Append(" need=").Append(need.ToString("F3"))
              .Append(" wrapResolvedH=").Append(v.resolvedStyle.height.ToString("F3"))
              .Append(" ctrlH=").Append(ch.ToString("F3"))
              .Append(" ctrlW=").Append(ctrl.resolvedStyle.width.ToString("F3")).Append("\n");
    }
    if (lb.text == "Colour ramp")
        sb.Append("RAMP wrapStyleH=").Append(stamped ? cur.value.value.ToString("F3") : "unset")
          .Append(" need=").Append(need.ToString("F3"))
          .Append(" wrapResolvedH=").Append(v.resolvedStyle.height.ToString("F3"))
          .Append(" wrapW=").Append(v.resolvedStyle.width.ToString("F3"))
          .Append(" ctrl=").Append(ctrl.GetType().Name).Append(" ctrlH=").Append(ch.ToString("F3")).Append(" ctrlW=").Append(ctrl.resolvedStyle.width.ToString("F3"))
          .Append(" labelW=").Append(lb.resolvedStyle.width.ToString("F3")).Append("\n");
}
sb.Append("unsettled fields=").Append(n).Append("\n");
return sb.ToString();
