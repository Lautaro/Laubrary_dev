// Does a section-header toggle (a pure VIEW change) discard a data edit made just before it?
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo assetF=null; for (var t=w.GetType(); t!=null && assetF==null; t=t.BaseType) assetF=t.GetField("asset",BFi);
var a = assetF.GetValue(w);
System.Func<int> count = () => {
  var aa = assetF.GetValue(w);
  var rig = aa.GetType().GetField("lightRig", BFi).GetValue(aa);
  return ((System.Collections.ICollection)rig.GetType().GetField("lights", BFi).GetValue(rig)).Count; };
var sb = new System.Text.StringBuilder();
sb.Append("start=").Append(count()).Append("\n");
// toggle the Swarm section header off then on, the way wK-swarm did
var secT = ZType("ZuiSection");
UnityEngine.UIElements.Toggle sw = null;
foreach (var e in ZAll(w.rootVisualElement)) { if (!secT.IsInstanceOfType(e) || !ZDrawn(e)) continue;
  bool isSwarm=false; foreach (var c in ZAll(e)) { var l=c as UnityEngine.UIElements.Label; if (l!=null && l.ClassListContains("zui-section__title") && l.text=="Swarm") { isSwarm=true; break; } }
  if (!isSwarm) continue;
  foreach (var c in ZAll(e)) if (c is UnityEngine.UIElements.Toggle tg && c.ClassListContains("zui-section__toggle")) { sw = tg; break; }
  break; }
if (sw == null) return sb.Append("no swarm toggle").ToString();
bool b0 = sw.value; sw.value = !b0;
sb.Append("swarm ").Append(b0).Append(" -> ").Append(sw.value).Append(" | lights=").Append(count()).Append("\n");
return sb.ToString();
