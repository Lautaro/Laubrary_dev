// Where is Chunks' preview, and does it show anything? Report the window's IMGUI containers and the
// spec's own source/sprite state, plus every section header in document order.
var w = ZWin("ChunkWindow"); if (w == null) return "no window";
var sb = new System.Text.StringBuilder();
foreach (var e in ZAll(w.rootVisualElement))
{
    if (!ZDrawn(e)) continue;
    if (e is UnityEngine.UIElements.IMGUIContainer)
        sb.Append("IMGUI at ").Append(e.worldBound).Append(" | ").Append(ZPath(e)).Append("\n");
    if (e.GetType().Name == "ZuiSection")
        sb.Append("SECTION '").Append(ZFirstText(e)).Append("' y=").Append(e.worldBound.y.ToString("F0")).Append(" h=").Append(e.worldBound.height.ToString("F0")).Append("\n");
}
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo af = null;
for (var t = w.GetType(); t != null; t = t.BaseType) if (af == null) af = t.GetField("asset", BFi|System.Reflection.BindingFlags.DeclaredOnly);
var spec = af.GetValue(w);
sb.Append("spec fields: ");
foreach (var f in spec.GetType().GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public))
{
    var v = f.GetValue(spec);
    var il = v as System.Collections.IList;
    sb.Append(f.Name).Append("=").Append(il != null ? ("[" + il.Count + "]") : (v == null ? "<null>" : v.ToString())).Append("  ");
}
return sb.ToString();
