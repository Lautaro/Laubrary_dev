var sb=new System.Text.StringBuilder();
foreach (var wn in new string[]{"ZoeWindow","MirageWindow","LarderWindow","LatheWindow","SpriteFxStackWindow","TextSplashWindow"}) {
  var w=ZWin(wn); if (w==null) continue;
  sb.Append("=== ").Append(wn).Append("\n");
  int n=0;
  foreach (var e in ZAll(w.rootVisualElement)) {
    var b=e as UnityEngine.UIElements.Button; if (b==null || !ZDrawn(b)) continue;
    if (ZCls(b).Contains("zui-radio__btn")) continue;
    n++;
    sb.Append("  [").Append(b.enabledInHierarchy?"LIVE":"grey").Append("] '").Append(b.text).Append("' cls=").Append(ZCls(b))
      .Append(" y=").Append(b.worldBound.y.ToString("F0")).Append("\n");
  }
  sb.Append("  total=").Append(n).Append("\n");
}
return sb.ToString();
