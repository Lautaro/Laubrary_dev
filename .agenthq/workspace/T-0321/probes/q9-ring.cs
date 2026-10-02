var w = ZWin("ChunkWindow"); var sb=new System.Text.StringBuilder();
var BFi=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo af=null; for (var t=w.GetType(); t!=null&&af==null; t=t.BaseType) af=t.GetField("asset",BFi);
var asset = af.GetValue(w) as UnityEngine.Object;
foreach (var f in asset.GetType().GetFields()) if (f.Name.ToLower().Contains("ring")||f.Name.ToLower().Contains("radius")) sb.Append(f.Name).Append("=").Append(f.GetValue(asset)).Append("\n");
UnityEngine.UIElements.FloatField t2=null;
foreach (var e in ZAll(w.rootVisualElement)) { var f=e as UnityEngine.UIElements.FloatField; if (f!=null&&ZDrawn(f)&&(ZTip(f)??"").StartsWith("Ring only: distance")) { t2=f; break; } }
sb.Append("found=").Append(t2!=null).Append(" v=").Append(t2!=null?t2.value.ToString():"-").Append(" enabled=").Append(t2!=null?t2.enabledInHierarchy.ToString():"-").Append("\n");
if (t2!=null) { t2.value = 7.25f; }
foreach (var f in asset.GetType().GetFields()) if (f.Name.ToLower().Contains("ring")||f.Name.ToLower().Contains("radius")) sb.Append("after ").Append(f.Name).Append("=").Append(f.GetValue(asset)).Append("\n");
return sb.ToString();
