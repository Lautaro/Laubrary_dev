var sb=new System.Text.StringBuilder();
var w=ZWin("TextSplashWindow");
UnityEngine.UIElements.Button rb=null;
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text=="Recall…") { rb=b; sb.Append("found Recall at ").Append(b.worldBound).Append(" tip=").Append(ZTip(b)).Append("\n"); } }
if (rb!=null) ZClick(rb);
sb.Append("panel children after: ");
foreach (var e in ZAll(w.rootVisualElement.panel.visualTree)) if (ZCls(e).Contains("popover")||ZCls(e).Contains("zui-menu")) sb.Append(ZCls(e)).Append(" @").Append(e.worldBound).Append(" | ");
sb.Append("\n");
// any other editor window opened?
foreach (var x in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (x!=null) sb.Append(x.GetType().Name).Append(" ");
return sb.ToString();
