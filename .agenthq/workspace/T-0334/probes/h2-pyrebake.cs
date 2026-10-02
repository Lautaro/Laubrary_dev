var sb = new System.Text.StringBuilder();
var w = ZWin("PyreWindow");
object cur = null;
for (var t = w.GetType(); t != null; t = t.BaseType)
{ var p = t.GetProperty("Current", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.DeclaredOnly);
  if (p != null) { cur = p.GetValue(w); break; } }
sb.Append("Current=").Append(UnityEditor.AssetDatabase.GetAssetPath(cur as UnityEngine.Object)).Append("\n");
UnityEngine.UIElements.Button bake = null;
foreach (var e in ZAll(w.rootVisualElement)) { var b = e as UnityEngine.UIElements.Button; if (b != null && ZDrawn(b) && b.text == "Bake") bake = b; }
if (bake == null) return sb.Append("no Bake button drawn\n").ToString();
UnityEngine.UIElements.ScrollView sv = null;
for (var p = bake.hierarchy.parent; p != null; p = p.hierarchy.parent) { var s = p as UnityEngine.UIElements.ScrollView; if (s != null) { sv = s; break; } }
if (sv != null) sv.ScrollTo(bake);
sb.Append("bake rect=").Append(bake.worldBound).Append(" root=").Append(w.rootVisualElement.worldBound).Append(" inScroll=").Append(sv != null).Append("\n");
sb.Append("pressed -> ").Append(ZClick(bake)).Append("\n");
return sb.ToString();
