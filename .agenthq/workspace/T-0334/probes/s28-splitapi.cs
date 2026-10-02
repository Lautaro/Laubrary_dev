var t = typeof(UnityEngine.UIElements.TwoPaneSplitView);
var sb = new System.Text.StringBuilder();
var BF = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.DeclaredOnly;
sb.Append("-- fields --\n"); foreach (var f in t.GetFields(BF)) sb.Append("  ").Append(f.FieldType.Name).Append(" ").Append(f.Name).Append("\n");
sb.Append("-- properties --\n"); foreach (var p in t.GetProperties(BF)) sb.Append("  ").Append(p.PropertyType.Name).Append(" ").Append(p.Name).Append(" set=").Append(p.GetSetMethod(true)!=null).Append("\n");
sb.Append("-- methods --\n"); foreach (var m in t.GetMethods(BF)) if (!m.IsSpecialName) sb.Append("  ").Append(m.Name).Append("(").Append(m.GetParameters().Length).Append(")\n");
return sb.ToString();
