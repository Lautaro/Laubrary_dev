var t = ZType("ShaperWindow");
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.DeclaredOnly;
var sb = new System.Text.StringBuilder();
for (var x = t; x != null && x.Name != "EditorWindow"; x = x.BaseType) {
  foreach (var m in x.GetMethods(BFi)) {
    var n = m.Name.ToLower();
    if (n.Contains("add") || n.Contains("new") || n.Contains("save") || n.Contains("shape") || n.Contains("fill") || n.Contains("light") || n.Contains("swarm") || n.Contains("height") || n.Contains("border") || n.Contains("layer"))
      sb.AppendLine(x.Name + "." + m.Name + "(" + string.Join(", ", System.Array.ConvertAll(m.GetParameters(), p => p.ParameterType.Name + " " + p.Name)) + ")");
  }
}
return sb.ToString();
