System.Type Find(string n){ foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies()){ var x=a.GetType(n); if(x!=null) return x; } return null; }
var sb=new System.Text.StringBuilder();
foreach(var tn in new[]{"Laubrary.Shaper.Editor.ShaperFieldAudit","Laubrary.Shaper.Editor.ShaperFillAudit"}){
  var t=Find(tn); sb.AppendLine("== "+tn);
  foreach(var m in t.GetMethods(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static)){
    if(m.DeclaringType!=t) continue;
    sb.AppendLine("  "+m.Name+"("+string.Join(",", System.Array.ConvertAll(m.GetParameters(), pp=>pp.ParameterType.Name+" "+pp.Name))+")");
  }
}
return sb.ToString();
