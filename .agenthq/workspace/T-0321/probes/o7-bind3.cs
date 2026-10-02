var sb=new System.Text.StringBuilder();
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
// Chunk
var cw = ZWin("ChunkWindow"); var chunkT = ZType("ChunkSpec");
foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:ChunkSpec")) {
  var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
  if (!p.Contains("WallDebris")) continue;
  var obj = UnityEditor.AssetDatabase.LoadAssetAtPath(p, chunkT);
  System.Reflection.MethodInfo sa=null, rb=null;
  for (var t=cw.GetType(); t!=null; t=t.BaseType) { if (sa==null) sa=t.GetMethod("SetAsset", BFi|System.Reflection.BindingFlags.DeclaredOnly); if (rb==null) rb=t.GetMethod("Rebuild", BFi|System.Reflection.BindingFlags.DeclaredOnly); }
  sa.Invoke(cw, new object[]{ obj }); if (rb!=null) rb.Invoke(cw,null); cw.Repaint();
  sb.Append("chunk -> ").Append(p).Append("\n"); break; }
// Lauminary
var lauT = ZType("Lauminary");
foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:Lauminary")) {
  var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
  var obj = UnityEditor.AssetDatabase.LoadAssetAtPath(p, lauT);
  var namesF = lauT.GetField("versions") ?? lauT.GetField("lauminations") ?? lauT.GetField("animations");
  string first=null;
  foreach (var f in lauT.GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public)) sb.Append("lf:").Append(f.Name).Append(":").Append(f.FieldType.Name).Append(" ");
  sb.Append("\nlauminary=").Append(p).Append("\n"); break; }
return sb.ToString();
