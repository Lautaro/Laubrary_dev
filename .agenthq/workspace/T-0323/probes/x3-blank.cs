var sb=new System.Text.StringBuilder();
foreach (var wn in new string[]{"ShaperWindow","PyreWindow","LarderWindow","ZoeWindow","MirageWindow"}) {
  var w=ZWin(wn); if (w==null) { sb.Append(wn).Append(": closed\n"); continue; }
  System.Reflection.MethodInfo thumb=null; System.Type T=null;
  for (var t=w.GetType(); t!=null; t=t.BaseType)
    if (t.IsGenericType && t.Name.StartsWith("ZuiAssetWindow")) { T=t.GetGenericArguments()[0];
      thumb = t.GetMethod("Thumb", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.DeclaredOnly); break; }
  if (T==null) continue;
  var gs = UnityEditor.AssetDatabase.FindAssets("t:"+T.Name);
  int n=0, blank=0; var det=new System.Text.StringBuilder();
  foreach (var g in gs) {
    var o = UnityEditor.AssetDatabase.LoadAssetAtPath(UnityEditor.AssetDatabase.GUIDToAssetPath(g), T); if (o==null) continue;
    n++;
    var tex = thumb.Invoke(w, new object[]{o}) as UnityEngine.Texture2D;
    if (tex==null) { blank++; det.Append(o.name).Append("(null) "); continue; }
    bool empty=true;
    try { var px=tex.GetPixels(); for(int i=0;i<px.Length;i++) if (px[i].a>0.02f) { empty=false; break; } }
    catch { empty=false; }
    if (empty) { blank++; det.Append(o.name).Append("(transparent ").Append(tex.width).Append("x").Append(tex.height).Append(") "); }
    if (n>=10) break;
  }
  sb.Append(wn).Append(" T=").Append(T.Name).Append(" items=").Append(n).Append(" blankOrNull=").Append(blank).Append(" ").Append(det).Append("\n");
}
return sb.ToString();
