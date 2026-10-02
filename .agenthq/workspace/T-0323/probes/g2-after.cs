var sb=new System.Text.StringBuilder();
string wn=UnityEditor.EditorPrefs.GetString("T323.pressWin","");
string ap=UnityEditor.EditorPrefs.GetString("T323.pressAsset","");
var w=ZWin(wn); if (w==null) return "no window "+wn;
sb.Append("SETTLED ").Append(ZState(w,ap)).Append("\n");
// popover contents
if (w.rootVisualElement.panel!=null)
  foreach (var e in ZAll(w.rootVisualElement.panel.visualTree)) if (ZCls(e).Contains("zui-popover")) {
    sb.Append("POPOVER ").Append(e.worldBound).Append(" vis=").Append(e.resolvedStyle.visibility).Append("\n");
    int n=0; foreach (var d in ZAll(e)) { var t=d as UnityEngine.UIElements.TextElement; if (t!=null && !string.IsNullOrEmpty(t.text) && n<40) { sb.Append("   '").Append(t.text).Append("'\n"); n++; } }
  }
return sb.ToString();
