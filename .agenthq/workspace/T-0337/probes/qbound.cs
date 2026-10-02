var w = ZWin("ShaperWindow");
if (w == null) return "NO SHAPER WINDOW";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo assetF=null; for(var t=w.GetType();t!=null;t=t.BaseType) if(assetF==null) assetF=t.GetField("asset",BFi);
var d = assetF.GetValue(w) as UnityEngine.ScriptableObject;
var sb=new System.Text.StringBuilder();
sb.Append("window pos=").Append(w.position).Append(" bound=").Append(d==null?"<null>":d.name).Append('\n');
if(d!=null){ var l=d.GetType().GetField("layers",BFi).GetValue(d) as System.Collections.IList;
  var root=l[0].GetType().GetField("root",BFi).GetValue(l[0]);
  foreach(var g in root.GetType().GetFields(BFi)){ var v=g.GetValue(root); if(v==null) continue;
    sb.Append("  root.").Append(g.Name).Append('=').Append(v.GetType().Name);
    var ff=v.GetType().GetField("form",BFi); if(ff!=null){ var fm=ff.GetValue(v); sb.Append("  form=").Append(fm==null?"<null>":fm.GetType().Name); }
    sb.Append('\n'); } }
int n=0; foreach (var e in ZAll(w.rootVisualElement)) n++;
sb.Append("elements=").Append(n).Append('\n');
foreach (var g in UnityEditor.AssetDatabase.FindAssets("AuditT337", new string[]{"Assets/Shaper"})) sb.Append("asset ").Append(UnityEditor.AssetDatabase.GUIDToAssetPath(g)).Append('\n');
return sb.ToString();
