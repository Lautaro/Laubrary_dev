var sb=new System.Text.StringBuilder();
UnityEditor.AssetDatabase.Refresh();
foreach (var n in new string[]{"CartographerWindow","LarderWindow","LatheWindow","LatheMoldWindow","SpriteFxStackWindow","TextSplashWindow","AnimationAsepriteWindow","ZoeWindow","MirageWindow","LauminationBuilderWindow","PyreWindow","LauminaryBrowserWindow","SpriteCatalogWindow","ChunkWindow"}) {
  var w = ZWin(n); if (w!=null) { w.Close(); sb.Append("closed ").Append(n).Append(" "); } }
var sw = ZWin("ShaperWindow");
if (sw!=null) { sw.titleContent = new GUIContent("Shaper"); sb.Append("\nshaper title restored"); }
UnityEditor.Undo.ClearAll();
return sb.ToString();
