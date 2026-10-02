var sb = new System.Text.StringBuilder();
foreach (var n in new string[]{"LatheWindow","TextSplashWindow"})
{
    var w = ZWin(n);
    foreach (var e in ZAll(w.rootVisualElement))
    {
        if (!ZDrawn(e) || !(e is UnityEditor.UIElements.ObjectField of)) continue;
        var lab = UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Label>(of, null, "unity-object-field-display__label");
        if (lab == null) continue;
        float need = lab.MeasureTextSize(lab.text ?? "", 0f, UnityEngine.UIElements.VisualElement.MeasureMode.Undefined, 0f, UnityEngine.UIElements.VisualElement.MeasureMode.Undefined).x;
        if (need <= lab.contentRect.width + 1.5f) continue;
        sb.Append(n).Append(" field '").Append(lab.text).Append("' need=").Append(need.ToString("F0"))
          .Append(" ownW=").Append(of.resolvedStyle.width.ToString("F0")).Append("\n");
        int i = 0;
        for (var p = e.hierarchy.parent; p != null && i < 6; p = p.hierarchy.parent, i++)
            sb.Append("    ^").Append(i).Append(" ").Append(p.GetType().Name).Append(" cls=").Append(ZCls(p))
              .Append(" contentW=").Append(p.contentRect.width.ToString("F0"))
              .Append(" dir=").Append(p.resolvedStyle.flexDirection).Append(" kids=").Append(p.hierarchy.childCount).Append("\n");
    }
}
return sb.ToString();
