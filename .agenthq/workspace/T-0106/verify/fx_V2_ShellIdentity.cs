System.Type Find(string n){ foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies()){ var x=a.GetType(n); if(x!=null) return x; } return null; }
var t = Find("Laubrary.Shaper.Editor.ShaperFieldAudit");
var sb=new System.Text.StringBuilder();
sb.AppendLine((string)t.GetMethod("V2_ShellIdentity").Invoke(null,null));
System.IO.File.AppendAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0106\verify\FIELD-AUDIT.txt", sb.ToString());
return "wrote " + sb.Length + " chars";
