// ZState(win, assetPath) — a comparable snapshot: asset dirty + serialized JSON + popover count + asset file count
System.Func<UnityEditor.EditorWindow,string,string> ZState = (w, path) => {
  var sb=new System.Text.StringBuilder();
  var obj = string.IsNullOrEmpty(path)? null : UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
  if (obj!=null) {
    sb.Append("dirty=").Append(UnityEditor.EditorUtility.IsDirty(obj)).Append(" ");
    string json = UnityEditor.EditorJsonUtility.ToJson(obj);
    sb.Append("jsonLen=").Append(json.Length).Append(" jsonHash=").Append(json.GetHashCode().ToString("X8")).Append(" ");
  }
  int pop=0; int popCtl=0;
  if (w!=null && w.rootVisualElement.panel!=null) {
    foreach (var e in ZAll(w.rootVisualElement.panel.visualTree)) { if (ZCls(e).Contains("zui-popover")) { pop++; foreach(var d in ZAll(e)) if (ZIsCtrl(d)) popCtl++; } }
  }
  sb.Append("popovers=").Append(pop).Append(" popCtrls=").Append(popCtl).Append(" ");
  int nAssets = UnityEditor.AssetDatabase.FindAssets("", new string[]{"Assets/Shaper"}).Length;
  sb.Append("shaperAssets=").Append(nAssets).Append(" ");
  if (w!=null) sb.Append("elements=").Append(ZAll(w.rootVisualElement).Count).Append(" ");
  return sb.ToString();
};
System.Func<UnityEditor.EditorWindow,string,int,UnityEngine.UIElements.Button> ZFindBtn = (w, text, nth) => {
  int i=0;
  foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b==null||!ZDrawn(b)) continue; if (b.text!=text) continue; if (i==nth) return b; i++; }
  return null;
};
var sb=new System.Text.StringBuilder();
string wn=UnityEditor.EditorPrefs.GetString("T323.pressWin","LatheWindow");
string bt=UnityEditor.EditorPrefs.GetString("T323.pressText","Twist");
string ap=UnityEditor.EditorPrefs.GetString("T323.pressAsset","");
var w=ZWin(wn); if (w==null) return "no window";
sb.Append("BEFORE ").Append(ZState(w,ap)).Append("\n");
UnityEngine.UIElements.VisualElement hit=null;
foreach (var e in ZAll(w.rootVisualElement.panel.visualTree)) if (ZCls(e).Contains("zui-popover") && e.worldBound.width<600) {
  foreach (var d in ZAll(e)) { var t=d as UnityEngine.UIElements.TextElement; if (t!=null && t.text==bt) { hit = (d.hierarchy.parent!=null && (d.hierarchy.parent is UnityEngine.UIElements.Button))? d.hierarchy.parent : d; } }
}
if (hit==null) return sb.Append("no item '").Append(bt).Append("'\n").ToString();
sb.Append("item ").Append(hit.GetType().Name).Append(" ").Append(hit.worldBound).Append("\n");
ZClick(hit);
sb.Append("AFTER  ").Append(ZState(w,ap)).Append("\n");
return sb.ToString();
