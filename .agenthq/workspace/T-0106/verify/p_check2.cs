var sb=new System.Text.StringBuilder();
sb.AppendLine("compileFailed = " + UnityEditor.EditorUtility.scriptCompilationFailed);
System.Type Find(string n){ foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies()){ var x=a.GetType(n); if(x!=null) return x; } return null; }
var t = Find("Laubrary.Shaper.Editor.ShaperFillAudit");
sb.AppendLine("FT21 present = " + (t!=null && t.GetMethod("FT21_ExclusivityIsConservative")!=null));
return sb.ToString();
