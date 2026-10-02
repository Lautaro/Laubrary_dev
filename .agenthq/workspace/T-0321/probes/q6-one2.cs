var w = ZWin("ChunkWindow"); var sb=new System.Text.StringBuilder();
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo af=null; for (var t=w.GetType(); t!=null&&af==null; t=t.BaseType) af=t.GetField("asset",BFi);
var asset = af.GetValue(w) as UnityEngine.Object;
var gf = asset.GetType().GetField("gravity");
sb.Append("gravity before=").Append(gf.GetValue(asset)).Append("\n");
UnityEngine.UIElements.FloatField target=null;
foreach (var e in ZAll(w.rootVisualElement)) { var f = e as UnityEngine.UIElements.FloatField;
  if (f!=null && ZDrawn(f) && Mathf.Abs(f.value - 20f) < 0.001f) { target=f; break; } }
sb.Append("target=").Append(target!=null).Append(" parentChain=");
for (var p=target.hierarchy.parent; p!=null; p=p.hierarchy.parent) { sb.Append(p.GetType().Name).Append(">"); if (p.GetType().Name.Contains("Zui")) break; }
sb.Append("\n");
target.value = 33.5f;
sb.Append("gravity after set=").Append(gf.GetValue(asset)).Append(" fieldNow=").Append(target.value).Append("\n");
return sb.ToString();
