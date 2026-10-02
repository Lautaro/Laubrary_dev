var sb=new System.Text.StringBuilder();
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
foreach (var wn in new string[]{"ShaperWindow","PyreWindow","LatheWindow","LarderWindow","SpriteFxStackWindow","TextSplashWindow","ZoeWindow","MirageWindow","CartographerWindow"}) {
  var w=ZWin(wn); if (w==null) { sb.Append(wn).Append(": closed\n"); continue; }
  System.Reflection.MethodInfo thumb=null; System.Type T=null;
  for (var t=w.GetType(); t!=null; t=t.BaseType) {
    if (t.IsGenericType && t.Name.StartsWith("ZuiAssetWindow")) { T=t.GetGenericArguments()[0];
      thumb = t.GetMethod("Thumb", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.DeclaredOnly); break; } }
  if (T==null) { sb.Append(wn).Append(": not a ZuiAssetWindow\n"); continue; }
  var gs = UnityEditor.AssetDatabase.FindAssets("t:"+T.Name);
  int n=0, got=0;
  var det=new System.Text.StringBuilder();
  foreach (var g in gs) {
    var o = UnityEditor.AssetDatabase.LoadAssetAtPath(UnityEditor.AssetDatabase.GUIDToAssetPath(g), T); if (o==null) continue;
    n++;
    object tex = thumb!=null ? thumb.Invoke(w, new object[]{o}) : null;
    if (tex!=null) got++; else det.Append(o.name).Append(", ");
    if (n>=8) break;
  }
  sb.Append(wn).Append(" T=").Append(T.Name).Append(" items=").Append(n).Append(" withThumb=").Append(got)
    .Append(got<n? ("  BLANK: "+det.ToString()) : "").Append("\n");
}
return sb.ToString();
