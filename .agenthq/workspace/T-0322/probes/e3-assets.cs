var sb=new System.Text.StringBuilder();
foreach (var tn in new string[]{"LevelAsset","WareSpec","LatheSpec","LatheMoldAsset","SpriteFxSpec","TextSplash"}) {
  var gs = UnityEditor.AssetDatabase.FindAssets("t:"+tn);
  sb.Append(tn).Append(" (").Append(gs.Length).Append("): ");
  for (int i=0;i<gs.Length && i<4;i++) sb.Append(UnityEditor.AssetDatabase.GUIDToAssetPath(gs[i])).Append(" | ");
  sb.Append("\n"); }
return sb.ToString();
