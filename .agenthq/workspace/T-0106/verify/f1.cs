System.Type Find(string n){ foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies()){ var x=a.GetType(n); if(x!=null) return x; } return null; }
var t = Find("Laubrary.Shaper.Editor.ShaperFillAudit");
var sb=new System.Text.StringBuilder();
sb.AppendLine((string)t.GetMethod("FT1_SolidIdentity").Invoke(null,null));
sb.AppendLine((string)t.GetMethod("FT2_SrgbRoundTrip").Invoke(null,null));
sb.AppendLine((string)t.GetMethod("FT3_CrossKindIdentity").Invoke(null,null));
sb.AppendLine((string)t.GetMethod("FT4_RampByCoveragePassthrough").Invoke(null,null));
sb.AppendLine((string)t.GetMethod("FT5_VeilCannotCreateCoverage").Invoke(null,null));
sb.AppendLine((string)t.GetMethod("FT6_VeilZeroKills").Invoke(null,null));
sb.AppendLine((string)t.GetMethod("FT7_NearestAncestor").Invoke(null,null));
System.IO.File.AppendAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0106\verify\final.txt", sb.ToString());
return "wrote " + sb.Length + " chars";
