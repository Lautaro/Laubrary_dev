UnityEditor.AssetDatabase.Refresh();
var sb = new System.Text.StringBuilder();
sb.AppendLine("dataPath=" + UnityEngine.Application.dataPath);
sb.AppendLine("compileFailed=" + UnityEditor.EditorUtility.scriptCompilationFailed);
sb.AppendLine("compiling=" + UnityEditor.EditorApplication.isCompiling);
var t = System.Type.GetType("Laubrary.Shaper.ShaperLightLaw, com.Lautaro-Arino.Laubrary.Shaper");
sb.AppendLine("ShaperLightLaw=" + (t == null ? "NULL" : t.FullName));
System.IO.File.AppendAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0108\verify\probe.txt", sb.ToString() + "----\n");
return sb.ToString();
