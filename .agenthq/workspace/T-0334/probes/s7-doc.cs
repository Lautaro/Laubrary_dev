var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo assetF=null; for (var t=w.GetType(); t!=null && assetF==null; t=t.BaseType) assetF=t.GetField("asset",BFi);
var a = assetF.GetValue(w) as UnityEngine.Object;
var sb = new System.Text.StringBuilder();
sb.Append("asset=").Append(UnityEditor.AssetDatabase.GetAssetPath(a)).Append(" dirty=").Append(UnityEditor.EditorUtility.IsDirty(a)).Append("\n");
string json = UnityEditor.EditorJsonUtility.ToJson(a);
sb.Append("jsonLen=").Append(json.Length).Append("\n");
foreach (var key in new string[]{"\"kind\"","\"hasEdge\"","\"edge\"","\"height\"","\"lights\"","\"swarm\"","\"fills\"","\"border\""}) {
  int c=0,i=0; while ((i=json.IndexOf(key,i))>=0){c++;i+=key.Length;} sb.Append("  ").Append(key).Append(" x").Append(c).Append("\n"); }
sb.Append(ZDump("doc-json.txt", json)).Append("\n");
return sb.ToString();
