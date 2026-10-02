// Force the demo document back to what is ON DISK (git says the file itself was never written), so no
// in-memory edit of mine can ever be flushed by someone else's SaveAssets.
var sb = new System.Text.StringBuilder();
string p = "Assets/Demos/ShaperDemo/ShaperDemoDoc.asset";
var doc = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p);
sb.Append("before dirty=").Append(UnityEditor.EditorUtility.IsDirty(doc))
  .Append(" json=").Append(UnityEditor.EditorJsonUtility.ToJson(doc).GetHashCode().ToString("X8")).Append("\n");
UnityEditor.AssetDatabase.ImportAsset(p, UnityEditor.ImportAssetOptions.ForceUpdate);
doc = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p);
sb.Append("after  dirty=").Append(UnityEditor.EditorUtility.IsDirty(doc))
  .Append(" json=").Append(UnityEditor.EditorJsonUtility.ToJson(doc).GetHashCode().ToString("X8")).Append("\n");
UnityEditor.Undo.ClearAll();
return sb.ToString();
