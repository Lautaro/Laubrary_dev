var sb=new System.Text.StringBuilder();
var BFs = System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
foreach (var n in new string[]{"ZoeWindow","MirageWindow"}) {
  var t = ZType(n); var w = ZWin(n);
  if (w==null && t!=null) { foreach (var m in t.GetMethods(BFs|System.Reflection.BindingFlags.DeclaredOnly)) if (m.GetParameters().Length==0 && (m.Name=="Open"||m.Name=="ShowWindow")) { m.Invoke(null,null); break; } w = ZWin(n); }
  sb.Append(n).Append("=").Append(w!=null).Append("\n"); }
foreach (var tn in new string[]{"Zoe","MirageSetup","MirageSpec"}) { var gs=UnityEditor.AssetDatabase.FindAssets("t:"+tn); sb.Append(tn).Append("(").Append(gs.Length).Append("): "); for(int i=0;i<gs.Length&&i<3;i++) sb.Append(UnityEditor.AssetDatabase.GUIDToAssetPath(gs[i])).Append(" | "); sb.Append("\n"); }
return sb.ToString();
