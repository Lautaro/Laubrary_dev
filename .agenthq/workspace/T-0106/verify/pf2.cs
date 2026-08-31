System.Type Find(string n){ foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies()){ var x=a.GetType(n); if(x!=null) return x; } return null; }
var t = Find("Laubrary.Shaper.Editor.ShaperFieldAudit");
var sb=new System.Text.StringBuilder();
sb.AppendLine((string)t.GetMethod("V5_RatioCrossCheck").Invoke(null,null));
sb.AppendLine((string)t.GetMethod("V6_Anisotropy").Invoke(null,null));
sb.AppendLine((string)t.GetMethod("V7_LandmarkTrap").Invoke(null,null));
sb.AppendLine((string)t.GetMethod("V8_TileIndependence").Invoke(null,null));
sb.AppendLine((string)t.GetMethod("V9_ShelledInsideness").Invoke(null,null));
System.IO.File.AppendAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0106\verify\field_v.txt", sb.ToString());
return "wrote " + sb.Length + " chars";
