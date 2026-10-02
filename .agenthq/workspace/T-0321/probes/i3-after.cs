var win = ZWin("ShaperWindow"); var sb=new System.Text.StringBuilder();
UnityEngine.UIElements.VisualElement v2 = null;
foreach (var e in ZAll(win.rootVisualElement)) if (e.GetType().Name=="ZuiValue2DControl" && ZDrawn(e)) { v2=e; break; }
var parent = v2.hierarchy.parent;
sb.Append("after focus: v2=").Append(v2.worldBound).Append(" focused=").Append(win.rootVisualElement.panel.focusController.focusedElement==v2).Append("\n");
for (int i=0;i<parent.hierarchy.childCount;i++) sb.Append("  sib ").Append(parent.hierarchy[i].GetType().Name).Append(" ").Append(parent.hierarchy[i].worldBound).Append("\n");
return sb.ToString();
