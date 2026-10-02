var win = ZWin("PyreWindow"); if (win==null) return "no pyre";
var root = win.rootVisualElement;
var all = ZAll(root);
float ww = win.position.width;
int past = 0; float maxRight = 0; var sb = new System.Text.StringBuilder();
foreach (var e in all) { if (!ZDrawn(e)) continue; var wb = e.worldBound;
  if (wb.xMax > maxRight) maxRight = wb.xMax;
  if (wb.xMax > ww + ZTOL) { past++; if (past < 8) sb.AppendLine("  past: " + e.GetType().Name + " '" + ZCaption(e) + "' " + wb); } }
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo lpF=null, lpaneF=null, vsF=null;
for (var t = win.GetType(); t != null; t = t.BaseType) { if (lpF==null) lpF=t.GetField("leftPaneWidth",BFi); if (lpaneF==null) lpaneF=t.GetField("leftPane",BFi); if (vsF==null) vsF=t.GetField("verticalSplitter",BFi); }
var lpane = lpaneF.GetValue(win) as UnityEngine.UIElements.VisualElement;
var vs = vsF.GetValue(win) as UnityEngine.UIElements.VisualElement;
sb.Insert(0, "window=" + ww + " leftPaneWidth=" + lpF.GetValue(win) + " leftPane=" + (lpane!=null?lpane.worldBound.ToString():"?") + " splitter=" + (vs!=null?vs.worldBound.ToString():"?") + "\nrootPadding=" + (root.resolvedStyle.paddingLeft+root.resolvedStyle.paddingRight) + " dividerW=" + (vs!=null?vs.resolvedStyle.width:0) + "\ndrawn=" + all.Count + " pastWindow=" + past + " maxRight=" + maxRight.ToString("F1") + "\n");
return sb.ToString();
