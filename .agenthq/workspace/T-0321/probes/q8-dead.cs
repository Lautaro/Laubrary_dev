var w = ZWin("ChunkWindow"); var sb=new System.Text.StringBuilder();
foreach (var e in ZAll(w.rootVisualElement)) { var f=e as UnityEngine.UIElements.FloatField; if (f==null||!ZDisplayed(f)) continue;
  if (Mathf.Abs(f.value-3.37f)>0.02f && Mathf.Abs(f.value-4.74f)>0.02f) continue;
  sb.Append("val=").Append(f.value).Append(" cap='").Append(ZCaption(f)).Append("' tip='").Append(ZTip(f)).Append("' path=").Append(ZPath(f)).Append(" drawn=").Append(ZDrawn(f)).Append("\n"); }
return sb.ToString();
