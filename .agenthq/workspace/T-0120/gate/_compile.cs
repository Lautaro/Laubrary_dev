var sb = new System.Text.StringBuilder();
sb.AppendLine("scriptCompilationFailed=" + EditorUtility.scriptCompilationFailed);
sb.AppendLine("isCompiling=" + EditorApplication.isCompiling);
var t = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ChunksMock.Editor.ChunksMockWindow")).FirstOrDefault(x => x != null);
sb.AppendLine("mockType=" + (t == null ? "MISSING" : t.Assembly.GetName().Name));
if (t != null)
{
    var nested = t.GetNestedType("MockTimingTracks", System.Reflection.BindingFlags.NonPublic);
    sb.AppendLine("MockTimingTracks=" + (nested == null ? "MISSING" : "present"));
    var obr = t.GetMethod("OnBeforeRebuild", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    sb.AppendLine("OnBeforeRebuild override declaredBy=" + (obr == null ? "none" : obr.DeclaringType.Name));
    var refs = t.Assembly.GetReferencedAssemblies().Select(r => r.Name).OrderBy(x => x);
    sb.AppendLine("asmRefs=" + string.Join(",", refs));
}
sb.AppendLine("dataPath=" + Application.dataPath);
return sb.ToString();
