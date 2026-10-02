var win = ZWin("ShaperWindow"); var sb = new System.Text.StringBuilder();
foreach (var e in ZAll(win.rootVisualElement)) {
  var b = e as UnityEngine.UIElements.Button; if (b == null || !ZDrawn(b)) continue;
  // a shape picker is a button whose only child is an icon + a label
  string lbl = null; foreach (var c in ZAll(b)) { var l = c as UnityEngine.UIElements.Label; if (l != null && !string.IsNullOrEmpty(l.text)) { lbl = l.text; break; } }
  if (lbl == null) continue;
  if (!(b.tooltip ?? "").ToLower().Contains("shape")) continue;
  sb.AppendLine("PICKER '" + lbl + "' " + b.worldBound + " tip=" + (b.tooltip.Length>70?b.tooltip.Substring(0,70)+"…":b.tooltip) + " path=" + ZPath(b));
}
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo assetF=null; for (var t=win.GetType(); t!=null && assetF==null; t=t.BaseType) assetF=t.GetField("asset",BFi);
var doc = assetF.GetValue(win) as UnityEngine.Object;
var so = new UnityEditor.SerializedObject(doc);
var p = so.FindProperty("layers");
if (p != null && p.arraySize > 0) {
  var l0 = p.GetArrayElementAtIndex(0);
  var it = l0.Copy(); var end = l0.GetEndProperty(); int n = 0;
  while (it.NextVisible(true) && !UnityEditor.SerializedProperty.EqualContents(it, end) && n < 60) { n++;
    if (it.propertyPath.ToLower().Contains("kind") || it.propertyPath.ToLower().Contains("shape")) sb.AppendLine("  DATA " + it.propertyPath + " = " + (it.propertyType == UnityEditor.SerializedPropertyType.Enum ? it.enumValueIndex + "/" + (it.enumDisplayNames.Length>it.enumValueIndex?it.enumDisplayNames[it.enumValueIndex]:"?") : it.propertyType.ToString()));
  }
}
return sb.ToString();
