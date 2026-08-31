var sb = new System.Text.StringBuilder();
sb.Append("compiling=" + UnityEditor.EditorApplication.isCompiling);
sb.Append(" failed=" + UnityEditor.EditorUtility.scriptCompilationFailed);
var t = System.Type.GetType("Laubrary.Shaper.Editor.ShaperLightAudit, com.Lautaro-Arino.Laubrary.Shaper.Editor");
sb.Append(" audit=" + (t == null ? "NULL" : "ok:" + t.GetMethods(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static).Length));
return sb.ToString();
