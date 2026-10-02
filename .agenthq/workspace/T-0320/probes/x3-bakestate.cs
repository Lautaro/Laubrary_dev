var win = ZWin("ShaperWindow"); var sb = new System.Text.StringBuilder();
foreach (var e in ZAll(win.rootVisualElement)) {
  if (!ZDrawn(e)) continue; var b = e as UnityEngine.UIElements.Button; if (b == null) continue;
  if (b.text == "AnimationClip" || b.text == "ShaperClip" || b.text == "GIF" || b.text == "Sprite sheet PNG" || b.text == "GIF dither")
    sb.AppendLine(b.text + " on=" + b.ClassListContains("zui-togglebutton--on") + " enabled=" + b.enabledInHierarchy);
}
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
for (var t = win.GetType(); t != null && t.Name != "EditorWindow"; t = t.BaseType)
  foreach (var f in t.GetFields(BFi|System.Reflection.BindingFlags.DeclaredOnly))
    if (f.FieldType == typeof(bool) && (f.Name.ToLower().Contains("gif") || f.Name.ToLower().Contains("bake") || f.Name.ToLower().Contains("clip") || f.Name.ToLower().Contains("sheet")))
      sb.AppendLine("FIELD " + f.Name + " = " + f.GetValue(win));
return sb.ToString();
