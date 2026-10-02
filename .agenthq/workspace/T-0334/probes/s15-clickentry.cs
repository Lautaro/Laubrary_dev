// click the menu entry named by T334.pick in the column T334.col — the menu must already be OPEN and
// laid out (open it in a previous eval, or the entry's worldBound is NaN and the press does nothing).
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var panelRoot = w.rootVisualElement.panel == null ? null : w.rootVisualElement.panel.visualTree;
if (panelRoot == null) return "no panel";
string want = UnityEditor.EditorPrefs.GetString("T334.pick", "");
string wantCol = UnityEditor.EditorPrefs.GetString("T334.col", "");
var sb = new System.Text.StringBuilder();
int n = 0; UnityEngine.UIElements.VisualElement target = null; string seen = "";
foreach (var e in ZAll(panelRoot)) {
  if (!e.ClassListContains("zui-menu__item")) continue;
  n++;
  string label = null;
  foreach (var c in ZAll(e)) { var l=c as UnityEngine.UIElements.Label; if (l!=null && l.text!="✓" && !string.IsNullOrEmpty(l.text)) { label=l.text; break; } }
  if (want.Length == 0) { sb.Append(label).Append(" | "); continue; }
  if (label != want) continue;
  string col = "";
  for (var p=e.hierarchy.parent; p!=null; p=p.hierarchy.parent) {
    foreach (var c in p.hierarchy.Children()) { var l=c as UnityEngine.UIElements.Label; if (l!=null && l.ClassListContains("zui-menu__section")) { col=l.text; break; } }
    if (col.Length>0) break; }
  seen += col + "|";
  if (wantCol.Length==0 || col==wantCol) { target = e; break; }
}
sb.Append("items=").Append(n).Append("\n");
if (want.Length == 0) return sb.ToString();
if (target == null) return sb.Append("entry '").Append(want).Append("' not found (cols: ").Append(seen).Append(")").ToString();
sb.Append("entry rect=").Append(target.worldBound).Append(" drawn=").Append(ZDrawn(target)).Append("\n");
if (!ZDrawn(target)) return sb.Append("ENTRY NOT LAID OUT — the press would be a no-op").ToString();
ZClick(target);
return sb.Append("clicked '").Append(want).Append("'\n").ToString();
