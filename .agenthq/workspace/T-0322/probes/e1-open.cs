var sb=new System.Text.StringBuilder();
var BFs = System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
foreach (var n in new string[]{"CartographerWindow","LarderWindow","LatheWindow","SpriteFxStackWindow","TextSplashWindow","AnimationAsepriteWindow"}) {
  var t = ZType(n); if (t==null) { sb.Append(n).Append(": NO TYPE\n"); continue; }
  var w = ZWin(n);
  if (w==null) { string called=null;
    foreach (var m in t.GetMethods(BFs|System.Reflection.BindingFlags.DeclaredOnly)) {
      if (m.GetParameters().Length!=0 || m.ReturnType==typeof(void)==false && !typeof(UnityEditor.EditorWindow).IsAssignableFrom(m.ReturnType)) {}
      if (m.GetParameters().Length==0 && (m.Name=="Open"||m.Name=="ShowWindow"||m.Name=="OpenWindow"||m.Name=="Show")) { m.Invoke(null,null); called=m.Name; break; } }
    if (called==null) { foreach (var m in t.GetMethods(BFs|System.Reflection.BindingFlags.DeclaredOnly)) if (m.GetParameters().Length==0) sb.Append(n).Append(" cand:").Append(m.Name).Append(" "); sb.Append("\n"); }
    w = ZWin(n); sb.Append(n).Append(" opened via ").Append(called).Append(" -> ").Append(w!=null).Append("\n"); }
  else sb.Append(n).Append(" already open\n");
  if (w!=null) { w.position = new Rect(20,20,820,880); w.Repaint(); }
}
return sb.ToString();
