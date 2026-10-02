var win = ZWin("ShaperWindow"); var sb=new System.Text.StringBuilder();
UnityEngine.UIElements.VisualElement v2 = null, nextSib = null;
foreach (var e in ZAll(win.rootVisualElement)) if (e.GetType().Name=="ZuiValue2DControl" && ZDrawn(e)) { v2=e; break; }
if (v2==null) return "no Value2D";
var parent = v2.hierarchy.parent;
sb.Append("before: v2=").Append(v2.worldBound).Append("\n");
var sibs = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
for (int i=0;i<parent.hierarchy.childCount;i++) sibs.Add(parent.hierarchy[i]);
foreach (var s in sibs) sb.Append("  sib ").Append(s.GetType().Name).Append(" ").Append(s.worldBound).Append("\n");
v2.Focus();
return sb.ToString();
