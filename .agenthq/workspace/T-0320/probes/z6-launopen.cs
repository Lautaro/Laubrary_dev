var t = ZType("LauminationBuilderWindow"); if (t == null) return "no type";
var sb = new System.Text.StringBuilder();
foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic))
  if (m.Name.Contains("Open")) sb.AppendLine(m.Name + "(" + string.Join(", ", System.Array.ConvertAll(m.GetParameters(), p => p.ParameterType.Name)) + ")");
return sb.ToString();
