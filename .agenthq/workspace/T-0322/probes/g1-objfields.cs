var sb=new System.Text.StringBuilder();
int tot=0, shortN=0;
foreach (var wn in new string[]{"ShaperWindow","PyreWindow","LatheWindow","LarderWindow","SpriteFxStackWindow","TextSplashWindow","LauminationBuilderWindow","CartographerWindow","ChunkWindow","ZoeWindow","MirageWindow"}) {
  var w = ZWin(wn); if (w==null) continue;
  foreach (var e in ZAll(w.rootVisualElement)) {
    var of = e as UnityEditor.UIElements.ObjectField; if (of==null || !ZDrawn(of)) continue;
    tot++;
    UnityEngine.UIElements.Label lab=null;
    foreach (var k in ZAll(of)) if (k is UnityEngine.UIElements.Label L && L.ClassListContains("unity-object-field-display__label")) lab=L;
    if (lab==null) continue;
    float need = ZNeed(lab), have = ZHave(lab);
    if (need > have + 1.5f) { shortN++; sb.Append(wn).Append(" '").Append(lab.text).Append("' need=").Append(need.ToString("F0")).Append(" have=").Append(have.ToString("F0")).Append(" fieldW=").Append(of.worldBound.width.ToString("F0")).Append("\n"); }
  } }
sb.Append("objectFields=").Append(tot).Append(" truncated=").Append(shortN);
return sb.ToString();
