var sb=new System.Text.StringBuilder();
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
// Chunk type name?
foreach (var n in new string[]{"ChunkSpec","ChunkDef","Chunk","ChunkAsset"}) { var t=ZType(n); if (t!=null) sb.Append("type ").Append(n).Append("=").Append(t.FullName).Append(" assets=").Append(UnityEditor.AssetDatabase.FindAssets("t:"+t.FullName).Length).Append("/").Append(UnityEditor.AssetDatabase.FindAssets("t:"+n).Length).Append("\n"); }
System.Func<string,System.Type,string> bind = (winName, ty) => {
  var w = ZWin(winName); if (w==null) return winName+": no window\n";
  var guids = UnityEditor.AssetDatabase.FindAssets("t:"+ty.Name);
  if (guids.Length==0) return winName+": no assets of "+ty.Name+"\n";
  var obj = UnityEditor.AssetDatabase.LoadAssetAtPath(UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]), ty);
  System.Reflection.MethodInfo sa=null, rb=null;
  for (var t=w.GetType(); t!=null; t=t.BaseType) { if (sa==null) sa=t.GetMethod("SetAsset", BFi|System.Reflection.BindingFlags.DeclaredOnly); if (rb==null) rb=t.GetMethod("Rebuild", BFi|System.Reflection.BindingFlags.DeclaredOnly); }
  sa.Invoke(w, new object[]{ obj }); if (rb!=null) rb.Invoke(w,null); w.Repaint();
  return winName+" -> "+UnityEditor.AssetDatabase.GetAssetPath(obj)+"\n"; };
var chunkT = ZType("Chunk"); sb.Append("chunkT=").Append(chunkT!=null?chunkT.FullName:"-").Append("\n");
if (chunkT!=null) sb.Append(bind("ChunkWindow", chunkT));
// Laumination builder: how does it take a Lauminary?
var lb = ZWin("LauminationBuilderWindow");
foreach (var f in lb.GetType().GetFields(BFi|System.Reflection.BindingFlags.DeclaredOnly)) sb.Append("f:").Append(f.Name).Append(":").Append(f.FieldType.Name).Append(" ");
sb.Append("\n");
foreach (var m in lb.GetType().GetMethods(System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.DeclaredOnly)) sb.Append("S:").Append(m.Name).Append("(").Append(m.GetParameters().Length).Append(") ");
return sb.ToString();
