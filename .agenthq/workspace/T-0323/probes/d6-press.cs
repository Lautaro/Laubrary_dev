var sb=new System.Text.StringBuilder();
string wn = UnityEditor.EditorPrefs.GetString("T323.auditWin","LatheWindow");
var w = ZWin(wn); if (w==null) return "no "+wn;
// dirty dot state
foreach (var e in ZAll(w.rootVisualElement)) {
  var te=e as UnityEngine.UIElements.TextElement;
  if (te!=null && te.text=="\u25CF") sb.Append("DOT vis=").Append(te.resolvedStyle.visibility).Append(" op=").Append(te.resolvedStyle.opacity)
    .Append(" disp=").Append(te.resolvedStyle.display).Append(" rect=").Append(te.worldBound).Append(" color=").Append(te.resolvedStyle.color).Append("\n");
}
UnityEngine.UIElements.Button newBtn=null;
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text=="New") newBtn=b; }
sb.Append("newBtn=").Append(newBtn!=null).Append("\n");
if (newBtn!=null) { ZClick(newBtn); sb.Append("pressed\n"); }
return sb.ToString();
