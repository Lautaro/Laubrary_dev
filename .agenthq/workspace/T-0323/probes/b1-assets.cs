var sb=new System.Text.StringBuilder();
foreach (var tn in new string[]{"WareSpec","LatheSpec","LatheMoldAsset","SpriteFxSpec","TextSplash","Zoe","MirageScene","ShaperDocument","PyreSpec"}) {
  var gs = UnityEditor.AssetDatabase.FindAssets("t:"+tn);
  sb.Append(tn).Append(" (").Append(gs.Length).Append("): ");
  for (int i=0;i<gs.Length && i<5;i++) sb.Append(UnityEditor.AssetDatabase.GUIDToAssetPath(gs[i])).Append(" | ");
  sb.Append("\n"); }
sb.Append("-- Assets/Shaper --\n");
if (System.IO.Directory.Exists("Assets/Shaper")) foreach (var f in System.IO.Directory.GetFiles("Assets/Shaper")) sb.Append(f).Append("\n");
return sb.ToString();
