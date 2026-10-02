var win = ZWin("PyreWindow");
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
var sb=new System.Text.StringBuilder();
var pv = win.GetType().GetField("preview", BFi).GetValue(win) as UnityEngine.UIElements.VisualElement;
pv.style.visibility = UnityEngine.UIElements.Visibility.Visible;
foreach (var n in new string[]{"lastPreviewView","swarmRect","previewBackdropRect"}) {
  var f = win.GetType().GetField(n, BFi); if (f==null) { sb.Append(n).Append("=<none> "); continue; }
  sb.Append(n).Append("=").Append(f.GetValue(win)).Append("\n");
}
sb.Append("preview local=").Append(pv.layout).Append(" world=").Append(pv.worldBound).Append(" contentRect=").Append(pv.contentRect).Append("\n");
var BF2 = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
var sf = win.GetType().GetField("s", BF2);
sb.Append("previewShowFrame=");
var af = win.GetType().GetField("asset", BF2); var a = af!=null?af.GetValue(win):null;
if (a!=null) { var pf = a.GetType().GetField("previewShowFrame"); sb.Append(pf!=null?pf.GetValue(a).ToString():"?"); }
return sb.ToString();
