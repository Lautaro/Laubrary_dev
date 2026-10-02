var w = ZWin("ChunkWindow");
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo af=null; for (var t=w.GetType(); t!=null&&af==null; t=t.BaseType) af=t.GetField("asset",BFi);
var asset = af.GetValue(w) as UnityEngine.Object;
var sb=new System.Text.StringBuilder();
sb.Append("asset=").Append(UnityEditor.AssetDatabase.GetAssetPath(asset)).Append(" dirty=").Append(UnityEditor.EditorUtility.IsDirty(asset)).Append("\n");
// dump a handful of floats
var so = new UnityEditor.SerializedObject(asset); var it = so.GetIterator(); int n=0;
while (it.NextVisible(true) && n<28) { n++; if (it.propertyType==UnityEditor.SerializedPropertyType.Float || it.propertyType==UnityEditor.SerializedPropertyType.Integer)
  sb.Append(it.propertyPath).Append("=").Append(it.propertyType==UnityEditor.SerializedPropertyType.Float?it.floatValue.ToString():it.intValue.ToString()).Append("\n"); }
return sb.ToString();
