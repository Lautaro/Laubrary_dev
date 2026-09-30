var checks = new System.Collections.Generic.List<string>();
void Check(bool ok, string message) { if (!ok) throw new System.InvalidOperationException(message); checks.Add(message); }
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.UISeparationPilot.FoundationWindow>())
{
    var root = w.rootVisualElement;
    bool reference = (bool)typeof(Laubrary.UISeparationPilot.FoundationWindow).GetField("_reference", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(w);
    bool scoped = !reference && root.ClassListContains("lau-tool-foundation-demo");
    var f = UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.FloatField>(root, "factory-float");
    var t = UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.TextField>(root, "factory-text");
    float pixel = 1f / UnityEditor.EditorGUIUtility.pixelsPerPoint + .001f;
    Check(UnityEngine.Mathf.Abs(f.resolvedStyle.width - (scoped ? 100 : 60)) < pixel, (reference ? "reference" : "candidate") + " float width " + f.resolvedStyle.width);
    Check(UnityEngine.Mathf.Abs(t.resolvedStyle.width - (scoped ? 230 : 200)) < pixel, (reference ? "reference" : "candidate") + " text width " + t.resolvedStyle.width);
    var flow = UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.VisualElement>(root, "foundation-column-flow");
    var columns = (System.Collections.IList)flow.GetType().GetField("_columns", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(flow);
    int expected = UnityEngine.Mathf.Clamp(UnityEngine.Mathf.FloorToInt(flow.contentRect.width / (scoped ? 340 : 220)), 1, 4);
    Check(columns.Count == expected, (reference ? "reference" : "candidate") + " responsive columns " + columns.Count);
    var first = (UnityEngine.UIElements.VisualElement)columns[0];
    Check(first.childCount > 0, "flow retained children");
    if (columns.Count > 1) { float gap = ((UnityEngine.UIElements.VisualElement)columns[1]).resolvedStyle.marginLeft; Check(UnityEngine.Mathf.Abs(gap - (scoped ? 18 : 6)) < pixel, "resolved gutter " + gap + " matches scope within one display pixel"); }
    if (!reference) Check(((Laubrary.Zui.ZuiColumnFlow)flow).ColumnWidth == 220, "public caller fallback unchanged");
}
return string.Join("\n", checks);
