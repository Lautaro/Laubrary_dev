var sb=new System.Text.StringBuilder();
foreach (var n in new string[]{"LarderWindow","LatheWindow","SpriteFxStackWindow","TextSplashWindow","ZoeWindow","MirageWindow","PyreWindow","AnimationAsepriteWindow","PopupWindow"})
  foreach (var x in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (x!=null && x.GetType().Name==n) { x.Close(); sb.Append("closed ").Append(n).Append("\n"); }
var w=ZWin("ShaperWindow"); if (w!=null) { w.titleContent=new UnityEngine.GUIContent("Shaper"); }
UnityEditor.Undo.ClearAll();
var t=ZType("LogEntries"); var m=t.GetMethod("Clear", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
if (m!=null) m.Invoke(null,null);
var doc=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Demos/ShaperDemo/ShaperDemoDoc.asset");
sb.Append("demoDirty=").Append(UnityEditor.EditorUtility.IsDirty(doc)).Append("\n");
int dirty=0; var names=new System.Text.StringBuilder();
foreach (var o in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.ScriptableObject>()) if (o!=null && UnityEditor.EditorUtility.IsDirty(o) && !string.IsNullOrEmpty(UnityEditor.AssetDatabase.GetAssetPath(o))) { dirty++; names.Append(UnityEditor.AssetDatabase.GetAssetPath(o)).Append(" | "); }
sb.Append("dirtySOs=").Append(dirty).Append(" ").Append(names).Append("\n");
sb.Append("compileFailed=").Append(UnityEditor.EditorUtility.scriptCompilationFailed).Append(" isPlaying=").Append(UnityEngine.Application.isPlaying).Append("\n");
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
return sb.ToString();
