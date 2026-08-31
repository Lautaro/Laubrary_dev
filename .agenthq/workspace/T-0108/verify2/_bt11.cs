string o = "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0108/verify2/bt11.txt";
System.Type t=null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()){ var x=a.GetType("Laubrary.Shaper.Editor.ShaperBorderAudit"); if(x!=null){t=x;break;} }
var m = t.GetMethod("BT11_Ordering", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static);
string r;
try { r=(string)m.Invoke(null,null); } catch(System.Exception e){ r="EXCEPTION "+e.GetBaseException(); }
System.IO.File.WriteAllText(o, r);
return "ok";
