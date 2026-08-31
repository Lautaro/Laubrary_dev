System.Type t = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var x = a.GetType("Laubrary.Shaper.Editor.ShaperBorderAudit"); if (x != null) { t = x; break; } }
var m = t.GetMethod("BT10_AnchorBoxDoesNotDilate") ?? t.GetMethod("BT10_AnchorDoesNotDilate");
if (m == null) { var names=new System.Text.StringBuilder(); foreach(var mi in t.GetMethods(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static)) if(mi.Name.StartsWith("BT10")||mi.Name.StartsWith("BT11")) names.Append(mi.Name+" "); return "NOT FOUND: "+names; }
return (string)m.Invoke(null,null);
