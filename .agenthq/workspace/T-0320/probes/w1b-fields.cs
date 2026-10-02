var win = ZWin("ShaperWindow"); if (win==null) return "no shaper";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.DeclaredOnly;
var sb = new System.Text.StringBuilder();
for (var t = win.GetType(); t != null && t.Name != "EditorWindow"; t = t.BaseType)
  foreach (var f in t.GetFields(BFi)) if (typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType) && !typeof(UnityEngine.UIElements.VisualElement).IsAssignableFrom(f.FieldType))
    sb.AppendLine(t.Name + "." + f.Name + " : " + f.FieldType.Name + " = " + (f.GetValue(win)?.ToString() ?? "null"));
foreach (var t2 in new string[]{""}) {}
for (var t = win.GetType(); t != null && t.Name != "EditorWindow"; t = t.BaseType)
  foreach (var p in t.GetProperties(BFi)) if (typeof(UnityEngine.Object).IsAssignableFrom(p.PropertyType)) sb.AppendLine("PROP " + t.Name + "." + p.Name + " = " + (p.GetValue(win)?.ToString() ?? "null"));
return sb.ToString();
