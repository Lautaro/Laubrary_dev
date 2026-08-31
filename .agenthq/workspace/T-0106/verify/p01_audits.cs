var sb = new System.Text.StringBuilder();
System.Type Find(string n){ foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies()){ var x=a.GetType(n); if(x!=null) return x; } return null; }
var fieldAudit = Find("Laubrary.Shaper.Editor.ShaperFieldAudit");
var fillAudit  = Find("Laubrary.Shaper.Editor.ShaperFillAudit");
sb.AppendLine("### FIELD AUDIT (T-0105 regression) ###");
sb.AppendLine((string)fieldAudit.GetMethod("RunAll").Invoke(null, null));
sb.AppendLine("### FILL AUDIT ###");
sb.AppendLine((string)fillAudit.GetMethod("RunAll").Invoke(null, null));
return sb.ToString();
