var sb = new System.Text.StringBuilder();
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
object cur = null;
for (var t = w.GetType(); t != null; t = t.BaseType) {
  var p = t.GetProperty("Current", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.DeclaredOnly);
  if (p != null) { cur = p.GetValue(w); break; } }
var obj = cur as UnityEngine.Object;
sb.Append("Current=").Append(obj == null ? "<null>" : UnityEditor.AssetDatabase.GetAssetPath(obj)).Append("\n");
if (obj != null) {
  var doc = obj; var ty = doc.GetType();
  foreach (var f in new string[]{"frameCount","frameRate","phase01","canvasWidth","canvasHeight"}) {
    var fi = ty.GetField(f, System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
    if (fi != null) sb.Append("  ").Append(f).Append("=").Append(fi.GetValue(doc)).Append("\n");
  }
  var so = new UnityEditor.SerializedObject(doc);
  var it = so.GetIterator(); int n=0;
  while (it.NextVisible(true) && n < 400) { n++; }
  sb.Append("  serializedVisibleProps=").Append(n).Append("\n");
}
// section chips
foreach (var e in ZAll(w.rootVisualElement)) {
  if (ZCls(e).Contains("zui-section") && e.GetType().Name.Contains("Section")) sb.Append("SECTION '").Append(ZFirstText(e)).Append("' drawn=").Append(ZDrawn(e)).Append("\n");
}
return sb.ToString();
