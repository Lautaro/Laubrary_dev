var sb=new System.Text.StringBuilder();
foreach (var wn in new string[]{"ShaperWindow","LatheWindow","TextSplashWindow","ZoeWindow"}) {
 var w=ZWin(wn); if (w==null) continue;
 foreach (var e in ZAll(w.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text=="Close browser") { ZClick(b); sb.Append(wn).Append(" closed browser\n"); break; } }
}
return sb.ToString();
