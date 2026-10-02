var sb = new System.Text.StringBuilder();
var w = ZWin("ShaperWindow");
var docF = w.GetType().GetField("document", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy);
var doc = docF == null ? null : docF.GetValue(w) as UnityEngine.Object;
sb.Append("doc=").Append(doc == null ? "<null>" : UnityEditor.AssetDatabase.GetAssetPath(doc)).Append("\n");
UnityEngine.UIElements.Button bake = null;
foreach (var e in ZAll(w.rootVisualElement)) { var b = e as UnityEngine.UIElements.Button; if (b != null && ZDrawn(b) && b.text == "Bake") bake = b; }
sb.Append("bake button found=").Append(bake != null);
if (bake != null) sb.Append(" enabled=").Append(bake.enabledInHierarchy).Append(" tip=").Append(ZTip(bake).Length > 100 ? ZTip(bake).Substring(0,100) : ZTip(bake));
sb.Append("\n");
if (bake == null)
{
  // it may be inside a folded/hidden section — report where
  foreach (var e in ZAll(w.rootVisualElement)) { var b = e as UnityEngine.UIElements.Button; if (b != null && b.text == "Bake")
    sb.Append("  Bake exists but drawn=").Append(ZDrawn(b)).Append(" displayed=").Append(ZDisplayed(b)).Append(" path=").Append(ZPath(b)).Append("\n"); }
}
return sb.ToString();
