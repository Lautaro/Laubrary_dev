var sb=new System.Text.StringBuilder();
if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Shaper/AuditT323Mirage.asset")==null)
  sb.Append(UnityEditor.AssetDatabase.CopyAsset("Assets/Mirage/MirageDemo.asset","Assets/Shaper/AuditT323Mirage.asset")?"copied mirage\n":"FAILED mirage\n");
UnityEditor.AssetDatabase.Refresh();
// open the six windows
var BFs = System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
foreach (var n in new string[]{"LarderWindow","LatheWindow","SpriteFxStackWindow","TextSplashWindow","ZoeWindow","MirageWindow","PyreWindow","AnimationAsepriteWindow"}) {
  var t = ZType(n); if (t==null) { sb.Append(n).Append(": NO TYPE\n"); continue; }
  var w = ZWin(n);
  if (w==null) { string called=null;
    foreach (var m in t.GetMethods(BFs|System.Reflection.BindingFlags.DeclaredOnly))
      if (m.GetParameters().Length==0 && (m.Name=="Open"||m.Name=="ShowWindow"||m.Name=="OpenWindow"||m.Name=="Show")) { m.Invoke(null,null); called=m.Name; break; }
    w = ZWin(n); sb.Append(n).Append(" opened via ").Append(called).Append(" -> ").Append(w!=null).Append("\n"); }
  else sb.Append(n).Append(" already open\n");
}
return sb.ToString();
