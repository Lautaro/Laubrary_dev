// One generic walk step, chosen by T334.step:
//   "edge" | "height" | "light" | "swarm"  — press the affordance through ZPress and report the data change
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo assetF=null; for (var t=w.GetType(); t!=null && assetF==null; t=t.BaseType) assetF=t.GetField("asset",BFi);
System.Func<object> DOC = () => assetF.GetValue(w);
System.Func<object> L0 = () => { var d=DOC(); var ls=d.GetType().GetField("layers",BFi).GetValue(d) as System.Collections.IList; return ls.Count>0?ls[0]:null; };
System.Func<object> ROOT = () => { var l=L0(); return l==null?null:l.GetType().GetField("root",BFi).GetValue(l); };
string step = UnityEditor.EditorPrefs.GetString("T334.step","edge");
System.Func<string> state = () => {
  var d=DOC(); var l=L0(); var r=ROOT();
  var rig=d.GetType().GetField("lightRig",BFi).GetValue(d);
  string s = "lights=" + ((System.Collections.ICollection)rig.GetType().GetField("lights",BFi).GetValue(rig)).Count;
  if (l!=null) s += " height=" + (l.GetType().GetField("height",BFi).GetValue(l)==null?"null":"set");
  if (r!=null) { var b=r.GetType().GetField("border",BFi).GetValue(r); var sw=r.GetType().GetField("swarm",BFi).GetValue(r);
    s += " edge=" + b.GetType().GetField("authored",BFi).GetValue(b) + "/" + b.GetType().GetField("enabled",BFi).GetValue(b);
    s += " swarm=" + sw.GetType().GetField("enabled",BFi).GetValue(sw); }
  return s; };
var sb=new System.Text.StringBuilder(); sb.Append("BEFORE ").Append(state()).Append("\n");
if (step == "swarm") {
  var secT = ZType("ZuiSection"); UnityEngine.UIElements.Toggle sw=null;
  foreach (var e in ZAll(w.rootVisualElement)) { if (!secT.IsInstanceOfType(e)||!ZDrawn(e)) continue;
    bool isS=false; foreach (var c in ZAll(e)) { var l=c as UnityEngine.UIElements.Label; if (l!=null&&l.ClassListContains("zui-section__title")&&l.text=="Swarm"){isS=true;break;} }
    if (!isS) continue; foreach (var c in ZAll(e)) if (c is UnityEngine.UIElements.Toggle tg && c.ClassListContains("zui-section__toggle")) { sw=tg; break; } break; }
  if (sw==null) return sb.Append("no Swarm header toggle").ToString();
  sw.value = !sw.value; sb.Append("swarm toggle -> ").Append(sw.value).Append("\n");
} else {
  string txt = step=="edge" ? "Add edge" : step=="height" ? "Height" : "+ Add light";
  UnityEngine.UIElements.Button btn=null;
  foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null&&ZDrawn(b)&&b.text==txt) { btn=b; break; } }
  if (btn==null) return sb.Append("NO BUTTON '").Append(txt).Append("'").ToString();
  sb.Append(ZPress(w, btn)).Append("\n");
}
sb.Append("AFTER  ").Append(state()).Append("\n");
return sb.ToString();
