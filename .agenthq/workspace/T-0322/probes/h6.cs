var sb=new System.Text.StringBuilder();
var w = ZWin("AnimationAsepriteWindow");
foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b==null||!ZDrawn(b)) continue;
  sb.Append("'").Append(b.text).Append("' en=").Append(b.enabledInHierarchy).Append(" tip='").Append(ZTip(b)).Append("'\n"); }
string rep = ZAudit(w, "AnimationAsepriteWindow-pop");
foreach (var k in new string[]{"elements","drawn","controls","captionShort","overflowParentX","overflowParentY","overflowWindow","noTooltip","inertNoReason"}) sb.Append(k).Append("=").Append(ZCount[k]).Append(" ");
return sb.ToString();
