var sb=new System.Text.StringBuilder();
foreach (var wn in new string[]{"ShaperWindow","PyreWindow","LatheWindow","LarderWindow","SpriteFxStackWindow","TextSplashWindow","ZoeWindow","MirageWindow"}) {
  var w=ZWin(wn); if (w==null) { sb.Append(wn).Append(": closed\n"); continue; }
  int imgs=0, withTex=0, blank=0; var names=new System.Text.StringBuilder();
  foreach (var e in ZAll(w.rootVisualElement)) {
    var im = e as UnityEngine.UIElements.Image; if (im==null || !ZDrawn(im)) continue;
    if (im.worldBound.width < 30) continue;   // glyphs/icons
    imgs++;
    var t = im.image as UnityEngine.Texture2D;
    if (t==null) { blank++; names.Append("[null] "); continue; }
    withTex++;
    names.Append(t.width).Append("x").Append(t.height).Append(" ");
  }
  sb.Append(wn).Append(" images>=30px=").Append(imgs).Append(" withTex=").Append(withTex).Append(" null=").Append(blank).Append(" : ").Append(names).Append("\n");
}
return sb.ToString();
