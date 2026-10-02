var sb=new System.Text.StringBuilder();
const string dst = "Assets/Shaper/AuditT321Chunk.asset";
if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(dst)==null)
  sb.Append("copy=").Append(UnityEditor.AssetDatabase.CopyAsset("Assets/Demos/ChunksDemo/WallDebris.asset", dst)).Append("\n");
UnityEditor.AssetDatabase.Refresh();
var chunkT = ZType("ChunkSpec");
var obj = UnityEditor.AssetDatabase.LoadAssetAtPath(dst, chunkT);
var w = ZWin("ChunkWindow");
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.MethodInfo sa=null, rb=null;
for (var t=w.GetType(); t!=null; t=t.BaseType) { if (sa==null) sa=t.GetMethod("SetAsset", BFi|System.Reflection.BindingFlags.DeclaredOnly); if (rb==null) rb=t.GetMethod("Rebuild", BFi|System.Reflection.BindingFlags.DeclaredOnly); }
sa.Invoke(w, new object[]{ obj }); if (rb!=null) rb.Invoke(w,null);
w.position = new Rect(20,20,900,880); w.Repaint();
// demo asset must be clean
var demo = UnityEditor.AssetDatabase.LoadAssetAtPath("Assets/Demos/ChunksDemo/WallDebris.asset", chunkT);
sb.Append("demoDirty=").Append(UnityEditor.EditorUtility.IsDirty(demo)).Append("\n");
sb.Append("bound=").Append(UnityEditor.AssetDatabase.GetAssetPath(obj)).Append("\n");
return sb.ToString();
