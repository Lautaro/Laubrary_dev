var win = ZWin("ShaperWindow"); if (win==null) return "no shaper";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.DeclaredOnly;
var sb = new System.Text.StringBuilder();
for (var x = win.GetType(); x != null && x.Name != "EditorWindow"; x = x.BaseType)
  foreach (var f in x.GetFields(BFi))
    if (typeof(UnityEngine.Texture).IsAssignableFrom(f.FieldType) || f.FieldType.Name.Contains("Stage") || f.FieldType.Name.Contains("Cache") || f.Name.ToLower().Contains("preview"))
      sb.AppendLine(x.Name + "." + f.Name + " : " + f.FieldType.Name + " = " + (f.GetValue(win)?.ToString() ?? "null"));
return sb.ToString();
