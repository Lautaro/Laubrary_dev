var sb=new System.Text.StringBuilder();
var w=ZWin("MirageWindow");
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
System.Reflection.MethodInfo rb=null, full=null;
for (var t=w.GetType(); t!=null; t=t.BaseType) { if (rb==null) rb=t.GetMethod("RebuildBody", BFi|System.Reflection.BindingFlags.DeclaredOnly); if (full==null) full=t.GetMethod("Rebuild", BFi|System.Reflection.BindingFlags.DeclaredOnly); }
sb.Append("RebuildBody in ").Append(rb!=null?rb.DeclaringType.Name:"?").Append("  Rebuild in ").Append(full!=null?full.DeclaringType.Name:"?").Append("\n");
if (rb!=null) rb.Invoke(w,null);
w.Repaint();
return sb.ToString();
