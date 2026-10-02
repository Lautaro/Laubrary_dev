var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
sb.Append("window=").Append(win.position.width.ToString("F1")).Append("x").Append(win.position.height.ToString("F1")).Append("\n");

System.Func<UnityEngine.UIElements.VisualElement, UnityEngine.UIElements.TwoPaneSplitView> FindSplit = null;
FindSplit = e => {
    if (e is UnityEngine.UIElements.TwoPaneSplitView tp) return tp;
    for (int i = 0; i < e.hierarchy.childCount; i++) { var r = FindSplit(e.hierarchy[i]); if (r != null) return r; }
    return null;
};
var split = FindSplit(win.rootVisualElement);
sb.Append("split found=").Append(split != null).Append("\n");
if (split != null)
{
    var left = split.ElementAt(0); var right = split.ElementAt(1);
    sb.Append("left  w=").Append(left.resolvedStyle.width.ToString("F1")).Append(" worldBound=").Append(left.worldBound.ToString()).Append("\n");
    sb.Append("right w=").Append(right.resolvedStyle.width.ToString("F1")).Append(" x=").Append(right.worldBound.xMin.ToString("F1")).Append("..").Append(right.worldBound.xMax.ToString("F1")).Append("\n");
    sb.Append("right pane INSIDE window (xMax<=").Append(win.position.width.ToString("F0")).Append(") = ").Append(right.worldBound.xMax <= win.position.width + 1f).Append("\n");
}
// the Play button, the thing a user must be able to reach in the right pane
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
Walk(win.rootVisualElement, all);
foreach (var v in all) if (v is UnityEngine.UIElements.Button b && b.text != null && b.text.Contains("Play"))
    sb.Append("Play button worldBound=").Append(b.worldBound.ToString()).Append(" fullyInside=")
      .Append(b.worldBound.xMax <= win.position.width + 1f && b.worldBound.yMax <= win.position.height + 1f).Append("\n");
sb.Append("persisted pref still=").Append(UnityEditor.EditorPrefs.GetFloat("ZUI.Split.shaper.window.split.v1", -1)).Append("\n");
return sb.ToString();
