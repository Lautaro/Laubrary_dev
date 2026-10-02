var win = ZWin("PyreWindow"); if (win==null) return "no pyre";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
var sb = new System.Text.StringBuilder();
for (var t = win.GetType(); t != null; t = t.BaseType)
  foreach (var f in t.GetFields(BFi|System.Reflection.BindingFlags.DeclaredOnly))
    if (f.FieldType == typeof(bool) && (f.Name.ToLower().Contains("play") || f.Name.ToLower().Contains("anim"))) sb.AppendLine(t.Name + "." + f.Name + " = " + f.GetValue(win));
return sb.ToString();
