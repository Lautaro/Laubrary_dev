var sb = new System.Text.StringBuilder();
// Pyre's persisted pane width back to its default, then close every tool window this task opened.
var pw = ZWin("PyreWindow");
if (pw != null) { var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
  System.Reflection.FieldInfo f = null; for (var t = pw.GetType(); t != null && f == null; t = t.BaseType) f = t.GetField("leftPaneWidth", BFi);
  if (f != null) { f.SetValue(pw, 360f); sb.AppendLine("Pyre leftPaneWidth -> 360"); } }
foreach (var w in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) {
  var n = w.GetType().Name;
  if (n == "PyreWindow" || n == "ChunkWindow" || n == "LauminationBuilderWindow") { w.Close(); sb.AppendLine("closed " + n); }
}
var sw = ZWin("ShaperWindow");
if (sw != null) { sw.titleContent = new GUIContent("Shaper"); sw.position = new UnityEngine.Rect(100, 20, 1500, 900); sb.AppendLine("Shaper window title restored"); }
UnityEditor.Undo.ClearAll();
var lt = System.Type.GetType("UnityEditor.LogEntries,UnityEditor");
lt.GetMethod("Clear", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public).Invoke(null, null);
sb.AppendLine("undo cleared, console cleared");
var demo = UnityEditor.AssetDatabase.LoadMainAssetAtPath("Assets/Demos/ShaperDemo/ShaperDemoDoc.asset");
sb.AppendLine("demo dirty=" + (demo != null && UnityEditor.EditorUtility.IsDirty(demo)));
sb.AppendLine("compileFailed=" + UnityEditor.EditorUtility.scriptCompilationFailed + " isPlaying=" + Application.isPlaying);
foreach (var f in System.IO.Directory.GetFiles(System.IO.Path.Combine(UnityEngine.Application.dataPath, "Shaper"))) sb.AppendLine("Assets/Shaper: " + System.IO.Path.GetFileName(f));
foreach (var f in System.IO.Directory.GetFiles(System.IO.Path.Combine(UnityEngine.Application.dataPath, "Pyre"))) sb.AppendLine("Assets/Pyre: " + System.IO.Path.GetFileName(f));
return sb.ToString();
