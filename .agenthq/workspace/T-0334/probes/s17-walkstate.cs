var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo assetF=null; for (var t=w.GetType(); t!=null && assetF==null; t=t.BaseType) assetF=t.GetField("asset",BFi);
var doc = assetF.GetValue(w);
var sb = new System.Text.StringBuilder();
sb.Append("asset=").Append(UnityEditor.AssetDatabase.GetAssetPath(doc as UnityEngine.Object)).Append(" dirty=").Append(UnityEditor.EditorUtility.IsDirty(doc as UnityEngine.Object)).Append("\n");
var layers = doc.GetType().GetField("layers", BFi).GetValue(doc) as System.Collections.IList;
sb.Append("layers=").Append(layers.Count).Append("\n");
var ly = layers[0];
var root = ly.GetType().GetField("root", BFi).GetValue(ly);
var prim = root.GetType().GetField("primitive", BFi).GetValue(root);
var bord = root.GetType().GetField("border", BFi).GetValue(root);
var fill = root.GetType().GetField("fill", BFi).GetValue(root);
var swarm = root.GetType().GetField("swarm", BFi).GetValue(root);
sb.Append("node.kind=").Append(root.GetType().GetField("kind",BFi).GetValue(root))
  .Append(" prim.kind=").Append(prim.GetType().GetField("kind",BFi).GetValue(prim)).Append("\n");
sb.Append("border authored=").Append(bord.GetType().GetField("authored",BFi).GetValue(bord)).Append(" enabled=").Append(bord.GetType().GetField("enabled",BFi).GetValue(bord)).Append("\n");
sb.Append("fill authored=").Append(fill.GetType().GetField("authored",BFi).GetValue(fill)).Append(" kind=").Append(fill.GetType().GetField("kind",BFi).GetValue(fill)).Append("\n");
foreach (var f in swarm.GetType().GetFields(BFi)) { var v=f.GetValue(swarm); if (v is bool) sb.Append("swarm.").Append(f.Name).Append("=").Append(v).Append("\n"); }
var h = ly.GetType().GetField("height", BFi).GetValue(ly);
sb.Append("layer.height=").Append(h==null?"<null>":h.GetType().Name).Append("\n");
var rig = doc.GetType().GetField("lightRig", BFi).GetValue(doc);
sb.Append("lights=").Append(((System.Collections.ICollection)rig.GetType().GetField("lights",BFi).GetValue(rig)).Count).Append("\n");
return sb.ToString();
