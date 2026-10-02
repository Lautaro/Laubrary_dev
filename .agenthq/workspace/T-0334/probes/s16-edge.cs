// Controlled: read the border state, press the Add edge / Remove edge button, read it again.
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo assetF=null; for (var t=w.GetType(); t!=null && assetF==null; t=t.BaseType) assetF=t.GetField("asset",BFi);
System.Func<string> state = () => {
  var doc = assetF.GetValue(w);
  var layers = doc.GetType().GetField("layers", BFi).GetValue(doc) as System.Collections.IList;
  var root = layers[0].GetType().GetField("root", BFi).GetValue(layers[0]);
  var b = root.GetType().GetField("border", BFi).GetValue(root);
  return "authored=" + b.GetType().GetField("authored", BFi).GetValue(b) + " enabled=" + b.GetType().GetField("enabled", BFi).GetValue(b); };
var sb = new System.Text.StringBuilder();
sb.Append("BEFORE ").Append(state()).Append("\n");
UnityEngine.UIElements.Button btn = null;
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && (b.text=="Add edge"||b.text=="Remove edge")) { btn=b; break; } }
if (btn == null) return sb.Append("no Add/Remove edge button").ToString();
var wr = w.rootVisualElement.worldBound;
sb.Append("btn '").Append(btn.text).Append("' ").Append(btn.worldBound).Append(" root=").Append(wr).Append("\n");
if (btn.worldBound.yMax > wr.yMax || btn.worldBound.yMin < wr.yMin) {
  UnityEngine.UIElements.ScrollView sv=null; for (var p=btn.hierarchy.parent;p!=null;p=p.hierarchy.parent){var s=p as UnityEngine.UIElements.ScrollView; if(s!=null){sv=s;break;}}
  if (sv==null) return sb.Append("off-window, no scrollview — UNREACHABLE").ToString();
  sv.ScrollTo(btn); w.Repaint(); return sb.Append("scrolled — rerun").ToString(); }
ZClick(btn);
sb.Append("AFTER  ").Append(state()).Append("\n");
return sb.ToString();
