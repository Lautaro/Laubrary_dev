var w = ZWin("ChunkWindow"); var sb=new System.Text.StringBuilder();
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo af=null; for (var t=w.GetType(); t!=null&&af==null; t=t.BaseType) af=t.GetField("asset",BFi);
var asset = af.GetValue(w) as UnityEngine.Object;
var gf = asset.GetType().GetField("gravity");
sb.Append("gravity before=").Append(gf.GetValue(asset)).Append("\n");
UnityEngine.UIElements.FloatField target=null;
foreach (var e in ZAll(w.rootVisualElement)) { var f = e as UnityEngine.UIElements.FloatField;
  if (f==null || !ZDrawn(f)) continue;
  if (Mathf.Abs(f.value - 21.37f) < 0.01f) { target=f; break; } }
if (target==null) { foreach (var e in ZAll(w.rootVisualElement)) { var f=e as UnityEngine.UIElements.FloatField; if (f!=null&&ZDrawn(f)) sb.Append("ff ").Append(f.value).Append(" cls=").Append(ZCls(f)).Append(" cap='").Append(ZCaption(f)).Append("'\n"); } return sb.ToString(); }
sb.Append("target cls=").Append(ZCls(target)).Append(" parent=").Append(target.hierarchy.parent.GetType().Name).Append(" cap='").Append(ZCaption(target)).Append("'\n");
target.value = 33.5f;
sb.Append("gravity after=").Append(gf.GetValue(asset)).Append("\n");
return sb.ToString();
