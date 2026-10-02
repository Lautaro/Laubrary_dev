var sb=new System.Text.StringBuilder();
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Func<string,string,string> bind = (winName, typeName) => {
  var w = ZWin(winName); if (w==null) return winName+": no window\n";
  var guids = UnityEditor.AssetDatabase.FindAssets("t:"+typeName);
  if (guids.Length==0) return winName+": no "+typeName+" assets\n";
  var obj = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
  System.Reflection.MethodInfo sa=null, rb=null;
  for (var t=w.GetType(); t!=null; t=t.BaseType) { if (sa==null) sa=t.GetMethod("SetAsset", BFi|System.Reflection.BindingFlags.DeclaredOnly); if (rb==null) rb=t.GetMethod("Rebuild", BFi|System.Reflection.BindingFlags.DeclaredOnly); }
  if (sa==null) return winName+": no SetAsset\n";
  sa.Invoke(w, new object[]{ obj }); if (rb!=null) rb.Invoke(w,null); w.Repaint();
  return winName+" -> "+UnityEditor.AssetDatabase.GetAssetPath(obj)+" (of "+guids.Length+")\n";
};
sb.Append(bind("ChunkWindow","Chunk"));
sb.Append(bind("ZoeWindow","Zoe"));
sb.Append(bind("LauminationBuilderWindow","Lauminary"));
var mw = ZWin("MirageWindow");
foreach (var m in mw.GetType().GetMethods(BFi|System.Reflection.BindingFlags.DeclaredOnly)) if (m.GetParameters().Length<=1) sb.Append("M:").Append(m.Name).Append(" ");
return sb.ToString();
