var sb=new System.Text.StringBuilder();
var t = ZType("PyreWindow"); sb.Append("type=").Append(t!=null?t.FullName:"NULL").Append("\n");
var w = ZWin("PyreWindow");
if (w==null) { var m = t.GetMethod("Open", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
  if (m==null) foreach (var mm in t.GetMethods(System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic)) sb.Append("st:").Append(mm.Name).Append(" ");
  else { m.Invoke(null,null); sb.Append("opened\n"); } }
else sb.Append("already open ").Append(w.position).Append("\n");
return sb.ToString();
