System.Type t = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var x = a.GetType("Laubrary.Shaper.Editor.ShaperBorderAudit"); if (x != null) { t = x; break; } }
var sb = new System.Text.StringBuilder();
foreach (var mi in t.GetMethods(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static))
  if (mi.Name.StartsWith("BT11") || mi.Name.StartsWith("BT10")) sb.AppendLine((string)mi.Invoke(null,null));
return sb.ToString();
