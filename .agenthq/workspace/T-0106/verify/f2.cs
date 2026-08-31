System.Type Find(string n){ foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies()){ var x=a.GetType(n); if(x!=null) return x; } return null; }
var t = Find("Laubrary.Shaper.Editor.ShaperFillAudit");
var sb=new System.Text.StringBuilder();
sb.AppendLine((string)t.GetMethod("FT8_TheGate").Invoke(null,null));
sb.AppendLine((string)t.GetMethod("FT9_ZeroAllocation").Invoke(null,null));
sb.AppendLine((string)t.GetMethod("FT10_Determinism").Invoke(null,null));
sb.AppendLine((string)t.GetMethod("FT11_TileIndependence").Invoke(null,null));
System.IO.File.AppendAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0106\verify\final.txt", sb.ToString());
return "wrote " + sb.Length + " chars";
