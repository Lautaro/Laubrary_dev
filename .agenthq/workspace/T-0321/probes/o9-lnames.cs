var sb=new System.Text.StringBuilder();
foreach (var g in UnityEditor.AssetDatabase.FindAssets("", new string[]{"Assets/Launimator/Lauminaries/ProtoGuy_25cf2dfd"})) {
  var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
  var o = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p);
  sb.Append(o!=null?o.GetType().Name:"?").Append(" ").Append(p).Append("\n");
  if (sb.Length>1500) break;
}
return sb.ToString();
