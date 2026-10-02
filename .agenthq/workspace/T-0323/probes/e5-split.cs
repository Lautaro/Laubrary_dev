var sb=new System.Text.StringBuilder();
var w=ZWin("TextSplashWindow");
foreach (var e in ZAll(w.rootVisualElement)) {
  var sv = e as UnityEngine.UIElements.TwoPaneSplitView; if (sv==null) continue;
  sb.Append("SPLIT bound=").Append(sv.worldBound).Append(" fixedIndex=").Append(sv.fixedPaneIndex).Append(" fixedDim=").Append(sv.fixedPaneInitialDimension).Append("\n");
  var mi = sv.GetType().GetMethod("CollapseChild"); 
  // try to widen
  var f = sv.GetType().GetField("m_FixedPane", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
  if (f!=null) { var fp = f.GetValue(sv) as UnityEngine.UIElements.VisualElement; if (fp!=null) { fp.style.width=700f; sb.Append("set fixed pane width 700\n"); } }
}
w.Repaint();
return sb.ToString();
