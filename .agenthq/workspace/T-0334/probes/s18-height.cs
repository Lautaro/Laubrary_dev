var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo assetF=null; for (var t=w.GetType(); t!=null && assetF==null; t=t.BaseType) assetF=t.GetField("asset",BFi);
System.Func<string> st = () => { var d=assetF.GetValue(w); var ls=d.GetType().GetField("layers",BFi).GetValue(d) as System.Collections.IList;
  var h=ls[0].GetType().GetField("height",BFi).GetValue(ls[0]); return h==null?"<null>":h.GetType().Name; };
var sb=new System.Text.StringBuilder(); sb.Append("BEFORE height=").Append(st()).Append("\n");
UnityEngine.UIElements.Button btn=null;
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text=="Height") { btn=b; break; } }
if (btn==null) return sb.Append("no Height button").ToString();
var wr=w.rootVisualElement.worldBound;
if (btn.worldBound.yMax>wr.yMax||btn.worldBound.yMin<wr.yMin) { UnityEngine.UIElements.ScrollView sv=null; for(var p=btn.hierarchy.parent;p!=null;p=p.hierarchy.parent){var s=p as UnityEngine.UIElements.ScrollView; if(s!=null){sv=s;break;}} if(sv==null) return sb.Append("off-window no scroll").ToString(); sv.ScrollTo(btn); w.Repaint(); return sb.Append("scrolled — rerun").ToString(); }
ZClick(btn);
sb.Append("AFTER  height=").Append(st()).Append("\n");
return sb.ToString();
