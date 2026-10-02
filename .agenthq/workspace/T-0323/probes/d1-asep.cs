var sb=new System.Text.StringBuilder();
var w=ZWin("AnimationAsepriteWindow"); if (w==null) return "no asep";
w.position=new Rect(20,20,820,400);
foreach (var e in ZAll(w.rootVisualElement)) {
  if (!ZDrawn(e)) continue;
  if (!ZIsCtrl(e)) continue;
  sb.Append(e.GetType().Name).Append(" '").Append(ZCaption(e)).Append("' enabled=").Append(e.enabledInHierarchy)
    .Append("\n  tip: ").Append((ZTip(e)??"").Replace("\n"," ")).Append("\n");
}
sb.Append("-- all text --\n");
foreach (var e in ZAll(w.rootVisualElement)) { var te=e as UnityEngine.UIElements.TextElement; if (te!=null && ZDrawn(te) && !string.IsNullOrEmpty(te.text)) sb.Append("  TXT '").Append(te.text).Append("'\n"); }
return sb.ToString();
