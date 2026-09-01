string o = "D:/UNITY/Laubrary Dev/.agenthq/workspace/T-0108/verify2/id.txt";
var sb = new System.Text.StringBuilder();
sb.AppendLine("dataPath=" + UnityEngine.Application.dataPath);
sb.AppendLine("compileFailed=" + UnityEditor.EditorUtility.scriptCompilationFailed);
int n=0; System.Type t=null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var x=a.GetType("Laubrary.Shaper.ShaperLightLaw"); if (x!=null){t=x;n++;} }
sb.AppendLine("ShaperLightLaw found in " + n + " assemblies; asm=" + (t==null?"NULL":t.Assembly.GetName().Name));
System.IO.File.WriteAllText(o, sb.ToString());
return "ok";
