var sb=new System.Text.StringBuilder();
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
var secT = ZType("ZuiSection"); var isOpen = secT.GetProperty("IsOpen");
foreach (var n in new string[]{"ZoeWindow","MirageWindow","ChunkWindow"}) {
  var w = ZWin(n); if (w==null) continue;
  w.position = new Rect(20,20,900,880);
  foreach (var e in ZAll(w.rootVisualElement)) if (secT.IsInstanceOfType(e)) isOpen.SetValue(e,true);
  w.Repaint();
  sb.Append(n).Append(" pos=").Append(w.position).Append("\n");
}
// Mirage needs an asset?
var mw = ZWin("MirageWindow");
System.Reflection.FieldInfo af=null; for (var t=mw.GetType(); t!=null&&af==null; t=t.BaseType) af=t.GetField("asset",BFi);
sb.Append("mirage asset=").Append(af!=null && af.GetValue(mw)!=null ? UnityEditor.AssetDatabase.GetAssetPath((UnityEngine.Object)af.GetValue(mw)) : "NULL").Append("\n");
if (af!=null && af.GetValue(mw)==null) {
  var tl = mw.GetType().GetProperty("TypeLabel", BFi); sb.Append("mirage typeLabel=").Append(tl!=null?tl.GetValue(mw):"?").Append("\n");
  var ft = af.FieldType; sb.Append("mirage assetType=").Append(ft.Name).Append(" count=").Append(UnityEditor.AssetDatabase.FindAssets("t:"+ft.Name).Length).Append("\n");
}
return sb.ToString();
