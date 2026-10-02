// What the Zoe window actually draws for ProtoGuy: sections, dropdowns (drawn or not), pickers.
var w = ZWin("ZoeWindow"); if (w == null) return "no ZoeWindow";
var sb = new System.Text.StringBuilder();
sb.Append("asset=");
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo af = null;
for (var t = w.GetType(); t != null; t = t.BaseType) if (af == null) af = t.GetField("asset", BFi|System.Reflection.BindingFlags.DeclaredOnly);
var a = af != null ? af.GetValue(w) as UnityEngine.Object : null;
sb.Append(a != null ? a.name : "<null>").Append(" elements=").Append(ZAll(w.rootVisualElement).Count).Append("\n");
int dd = 0;
foreach (var e in ZAll(w.rootVisualElement))
{
    var d = e as UnityEngine.UIElements.DropdownField;
    if (d == null) continue;
    dd++;
    sb.Append("DROPDOWN drawn=").Append(ZDrawn(d)).Append(" cap='").Append(ZCaption(d)).Append("' value='").Append(d.value)
      .Append("' enabled=").Append(d.enabledInHierarchy).Append(" choices=").Append(d.choices.Count).Append(" [");
    foreach (var c in d.choices) sb.Append(c).Append("|");
    sb.Append("]\n");
}
sb.Append("dropdowns=").Append(dd).Append("\n");
foreach (var e in ZAll(w.rootVisualElement))
{
    var s = e as UnityEngine.UIElements.TextElement;
    if (s == null || string.IsNullOrEmpty(s.text)) continue;
    if (s.text.Contains("Muzzle") || s.text.Contains("Waist") || s.text.Contains("unresolved") || s.text.Contains("None declared") || s.text.Contains("On Frame"))
        sb.Append("TXT drawn=").Append(ZDrawn(s)).Append(" '").Append(s.text).Append("'\n");
}
return sb.ToString();
