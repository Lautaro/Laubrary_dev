var w = ZWin("MirageWindow"); var sb=new System.Text.StringBuilder();
foreach (var e in ZAll(w.rootVisualElement)) { if (!ZDrawn(e)) continue;
  var wb=e.worldBound; if (wb.y < 460 || wb.y > 580) continue;
  if (!(e is UnityEngine.UIElements.TextField || e is UnityEngine.UIElements.Button || e.GetType().Name.Contains("Object"))) continue;
  sb.Append(e.GetType().Name).Append(" '").Append(ZOwnText(e)).Append("' en=").Append(e.enabledInHierarchy).Append(" tip='").Append((ZTip(e)??"").Length>70?ZTip(e).Substring(0,70)+"…":ZTip(e)).Append("' wb=").Append(wb).Append("\n"); }
return sb.ToString();
