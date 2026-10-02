var sb = new System.Text.StringBuilder();
var w = ZWin("ShaperWindow");
UnityEngine.UIElements.Button bake = null;
foreach (var e in ZAll(w.rootVisualElement)) { var b = e as UnityEngine.UIElements.Button; if (b != null && ZDrawn(b) && b.text == "Bake") bake = b; }
// what is bound, through the window's own Current
object cur = null;
for (var t = w.GetType(); t != null; t = t.BaseType)
{ var p = t.GetProperty("Current", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.DeclaredOnly);
  if (p != null) { cur = p.GetValue(w); break; } }
var obj = cur as UnityEngine.Object;
sb.Append("Current=").Append(obj == null ? "<null>" : UnityEditor.AssetDatabase.GetAssetPath(obj)).Append("\n");
sb.Append("pressed Bake -> ").Append(ZClick(bake)).Append("\n");
return sb.ToString();
