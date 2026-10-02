var sb=new System.Text.StringBuilder();
string bestS=null, bestT=null; int ls=0, lt=0;
foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:Sprite")) {
  var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
  foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(p)) { var s = o as UnityEngine.Sprite; if (s==null) continue; if (s.name.Length>ls){ls=s.name.Length;bestS=s.name+" @ "+p;} }
}
foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:Texture2D")) {
  var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
  var t = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(p);
  if (t!=null && t.name.Length>lt){lt=t.name.Length;bestT=t.name+" @ "+p;}
}
sb.Append("longest Sprite (").Append(ls).Append("): ").Append(bestS).Append("\n");
sb.Append("longest Texture2D (").Append(lt).Append("): ").Append(bestT).Append("\n");
return sb.ToString();
