var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();

System.Type zT = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == "Z" && t.Namespace != null && t.Namespace.Contains("Zui")) zT = t;
sb.Append("Z=").Append(zT == null ? "NULL" : zT.FullName).Append("\n");
foreach (var m in zT.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public))
    if (m.Name == "MiniRadio" || m.Name == "Field" || m.Name == "HGroup" || m.Name == "Box")
    {
        var ps = new System.Text.StringBuilder();
        foreach (var p in m.GetParameters()) ps.Append(p.ParameterType.Name).Append(' ').Append(p.Name).Append(p.IsOptional ? "=opt" : "").Append(", ");
        sb.Append("M ").Append(m.Name).Append("(").Append(ps.ToString()).Append(")\n");
    }
return sb.ToString();
