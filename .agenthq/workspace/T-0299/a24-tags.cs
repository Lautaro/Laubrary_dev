var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
System.Func<string, System.Type> FT = n => {
    foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t;
    return null; };
var libT = FT("LauTagLibrary"); var tagsT = FT("LauTags"); var fieldT = FT("LauTagField"); var pickerT = FT("LauTagPicker");
sb.Append("types: LauTagLibrary=").Append(libT != null).Append(" LauTags=").Append(tagsT != null)
  .Append(" LauTagField=").Append(fieldT != null).Append(" LauTagPicker=").Append(pickerT != null).Append("\n");
foreach (var tt in new System.Type[] { tagsT, libT })
{
    if (tt == null) continue;
    sb.Append("-- ").Append(tt.Name).Append(" public statics/methods:\n");
    foreach (var m in tt.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
    { var ps = new System.Text.StringBuilder(); foreach (var p in m.GetParameters()) ps.Append(p.ParameterType.Name).Append(' ').Append(p.Name).Append(", ");
      sb.Append("     ").Append(m.ReturnType.Name).Append(' ').Append(m.Name).Append('(').Append(ps.ToString()).Append(")\n"); }
}
return sb.ToString();
