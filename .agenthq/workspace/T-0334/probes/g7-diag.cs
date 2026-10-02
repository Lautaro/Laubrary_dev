var sb = new System.Text.StringBuilder();
var t = ZType("ZoeWindow");
sb.Append("ZoeWindow=").Append(t == null ? "<null>" : t.FullName).Append("\n");
for (var b = t; b != null; b = b.BaseType)
{
    sb.Append("  type ").Append(b.Name).Append(": ");
    foreach (var m in b.GetMethods(System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.DeclaredOnly))
        if (m.Name.StartsWith("All") || m.Name.StartsWith("Get")) sb.Append(m.Name).Append(" ");
    sb.Append("\n");
    if (b.Name == "EditorWindow") break;
}
return sb.ToString();
