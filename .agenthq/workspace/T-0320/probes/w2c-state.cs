var win = ZWin("ShaperWindow");
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
var sb = new System.Text.StringBuilder();
foreach (var n in new string[]{"creating","browsing","renaming","createText"}) {
  System.Reflection.FieldInfo f = null;
  for (var t = win.GetType(); t != null && f == null; t = t.BaseType) f = t.GetField(n, BFi);
  sb.AppendLine(n + " = " + (f != null ? (f.GetValue(win)?.ToString() ?? "null") : "NO FIELD"));
}
return sb.ToString();
