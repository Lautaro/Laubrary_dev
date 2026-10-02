// How many of the hosted form's authored fields are actually DRAWN on the Shape card?
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo assetF=null; for (var t=w.GetType(); t!=null && assetF==null; t=t.BaseType) assetF=t.GetField("asset",BFi);
var doc = assetF.GetValue(w);
var layers = doc.GetType().GetField("layers", BFi).GetValue(doc) as System.Collections.IList;
var root = layers[0].GetType().GetField("root", BFi).GetValue(layers[0]);
var comp = root.GetType().GetField("composite", BFi).GetValue(root);
var sb = new System.Text.StringBuilder();
if (comp == null) return "no composite";
var srcF = comp.GetType().GetField("source", BFi); var src = srcF == null ? null : srcF.GetValue(comp);
sb.Append("source=").Append(src == null ? "<null>" : src.GetType().Name).Append("\n");
if (src != null) {
  var formF = src.GetType().GetField("form", BFi); var form = formF == null ? null : formF.GetValue(src);
  sb.Append("form=").Append(form == null ? "<null>" : form.GetType().Name).Append("\n");
  if (form != null) {
    int nAuthored = 0;
    System.Action<object,int> count = null;
    var seen = new System.Collections.Generic.HashSet<object>();
    count = (o, d) => { if (o == null || d > 3) return;
      foreach (var f in o.GetType().GetFields(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance)) {
        var ft = f.FieldType;
        if (ft == typeof(float) || ft == typeof(int) || ft == typeof(bool) || ft.IsEnum || ft.Name == "ZUIValue") { nAuthored++; continue; }
        if (ft.IsClass && ft.Namespace != null && ft.Namespace.StartsWith("Laubrary")) { var v = f.GetValue(o); if (v != null && seen.Add(v)) count(v, d+1); }
        else if (ft.IsValueType && !ft.IsPrimitive && ft.Namespace != null && ft.Namespace.StartsWith("Laubrary")) { var v = f.GetValue(o); if (v != null) count(v, d+1); } } };
    count(form, 0);
    sb.Append("authored scalar/enum/bool/ZUIValue fields on the form (depth<=3): ").Append(nAuthored).Append("\n");
  }
}
// drawn leaf controls inside the Shape section
var secT = ZType("ZuiSection"); UnityEngine.UIElements.VisualElement shapeSec = null;
foreach (var e in ZAll(w.rootVisualElement)) { if (!secT.IsInstanceOfType(e)||!ZDrawn(e)) continue;
  foreach (var c in ZAll(e)) { var l=c as UnityEngine.UIElements.Label; if (l!=null&&l.ClassListContains("zui-section__title")&&l.text.StartsWith("Shape")) { shapeSec=e; break; } }
  if (shapeSec!=null) break; }
if (shapeSec == null) return sb.Append("no Shape section").ToString();
int leaf=0, boxes=0;
foreach (var e in ZAll(shapeSec)) { if (!ZDrawn(e)) continue; if (ZIsLeafCtrl(e)) leaf++; if (e.GetType().Name=="ZuiBox") boxes++; }
sb.Append("Shape section: drawn leaf controls=").Append(leaf).Append(" boxes=").Append(boxes).Append("\n");
return sb.ToString();
