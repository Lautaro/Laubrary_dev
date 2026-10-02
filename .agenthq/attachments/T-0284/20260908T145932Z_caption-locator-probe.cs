// A19 — for one caption, show every on-screen element carrying it with its ancestor chain.
var SB = new System.Text.StringBuilder();
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> WalkT = null;
WalkT = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) WalkT(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); WalkT(win.rootVisualElement, all);
System.Func<UnityEngine.UIElements.VisualElement, string> TextOf = v =>
{
    var p = v.GetType().GetProperty("text", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
    if (p != null && p.PropertyType == typeof(string)) { try { return (string)p.GetValue(v); } catch { } }
    return null;
};
foreach (var want in UnityEditor.EditorPrefs.GetString("A19.where").Split('|'))
{
    if (want == "") continue;
    SB.Append("=== \"").Append(want).Append("\" ===\n");
    foreach (var v in all)
    {
        if (v.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.None) continue;
        if (TextOf(v) != want) continue;
        bool hasTextChild = false;
        for (int i = 0; i < v.childCount; i++) if (!string.IsNullOrEmpty(TextOf(v[i]))) hasTextChild = true;
        if (hasTextChild) continue;
        var r = v.worldBound;
        var chain = new System.Collections.Generic.List<string>();
        for (var p = v; p != null && chain.Count < 9; p = p.parent)
        {
            var tt = TextOf(p);
            chain.Add(p.GetType().Name + (string.IsNullOrEmpty(tt) ? "" : "[" + tt + "]") + (string.IsNullOrEmpty(p.name) ? "" : "#" + p.name));
        }
        chain.Reverse();
        SB.Append("  @").Append((int)r.x).Append(',').Append((int)r.y)
          .Append(v.enabledInHierarchy ? "" : " DISABLED")
          .Append(" tip=\"").Append(v.tooltip ?? "").Append("\"\n    ").Append(string.Join(" / ", chain)).Append('\n');
    }
}
return SB.ToString();
