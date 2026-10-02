// Walk step: pick a Subject sprite through the window's own ObjectField, then press its own Resample.
var w = ZWin("ChunkWindow"); if (w == null) return "no window";
var sb = new System.Text.StringBuilder();
string spritePath = UnityEditor.EditorPrefs.GetString("T328.sprite", "");
if (string.IsNullOrEmpty(spritePath))
{
    foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:Sprite", new string[]{ "Assets/Demos" }))
    {
        var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
        var s = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(p);
        if (s != null && s.rect.width >= 16 && s.rect.height >= 16) { spritePath = p; break; }
    }
}
var spr = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
sb.Append("sprite=").Append(spr == null ? "<null>" : spr.name + " @ " + spritePath).Append("\n");
UnityEditor.UIElements.ObjectField subject = null;
foreach (var e in ZAll(w.rootVisualElement))
{
    var of = e as UnityEditor.UIElements.ObjectField;
    if (of == null || !ZDrawn(of)) continue;
    if (of.objectType != typeof(Sprite)) continue;
    string cap = ZCaption(of.hierarchy.parent);
    sb.Append("  objectfield near '").Append(cap).Append("' value=").Append(of.value == null ? "<null>" : of.value.name).Append("\n");
    if (cap == "Subject") subject = of;
}
if (subject == null) return sb.Append("no Subject field").ToString();
subject.value = spr;
sb.Append("set Subject -> ").Append(subject.value == null ? "<null>" : subject.value.name).Append("\n");
return sb.ToString();
