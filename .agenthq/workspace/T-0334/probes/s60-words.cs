// Live words audit over the bound Shaper document: raw-identifier captions, MicroSlider captions over
// 13 chars, on-screen explanatory paragraphs, and controls with no tooltip.
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
var sb = new System.Text.StringBuilder();
string tag = UnityEditor.EditorPrefs.GetString("T334.tag","x");
int raw=0, longCap=0, para=0, noTip=0, caps=0;
var rawRe = new System.Text.RegularExpressions.Regex(@"^[a-z][A-Za-z0-9]*$");
foreach (var e in ZAll(w.rootVisualElement)) {
  if (!ZDrawn(e)) continue;
  var te = e as UnityEngine.UIElements.TextElement;
  if (te != null && !string.IsNullOrEmpty(te.text)) {
    string t = te.text.Trim();
    // a raw identifier: one word, camelCase or snake_case, at least 4 chars, no space
    if ((t.IndexOf(' ') < 0 && t.Length >= 4 && (t.IndexOf('_') >= 0 || (rawRe.IsMatch(t) && System.Text.RegularExpressions.Regex.IsMatch(t, "[a-z][A-Z]"))))) {
      raw++; sb.Append("RAW-IDENT '").Append(t).Append("' cls=").Append(ZCls(te)).Append(" | ").Append(ZPath(te)).Append("\n"); }
    if (te.resolvedStyle.whiteSpace == UnityEngine.UIElements.WhiteSpace.Normal && t.Length > 80) {
      para++; sb.Append("PARAGRAPH (").Append(t.Length).Append(" chars) '").Append(t.Length>90?t.Substring(0,90):t).Append("…' | ").Append(ZPath(te)).Append("\n"); }
  }
  if (e.GetType().Name == "ZuiMicroSlider" || e.GetType().Name == "ZuiMicroMinMax") {
    foreach (var c in ZAll(e)) { if (!ZCls(c).Contains("__caption")) continue;
      var l = c as UnityEngine.UIElements.Label; if (l == null || string.IsNullOrEmpty(l.text)) continue;
      caps++;
      if (l.text.Length > 13) { longCap++; sb.Append("CAPTION>13 (").Append(l.text.Length).Append(") '").Append(l.text).Append("' | ").Append(ZPath(e)).Append("\n"); }
      break; }
  }
  if (ZIsCtrl(e) && string.IsNullOrEmpty(ZTip(e))) { noTip++; sb.Append("NO-TOOLTIP '").Append(ZCaption(e)).Append("'\n"); }
}
var head = tag + " | microCaptions=" + caps + " rawIdent=" + raw + " captionOver13=" + longCap + " paragraphs=" + para + " noTooltip=" + noTip + "\n";
ZDump("words-" + tag + ".txt", head + sb.ToString());
return head + (sb.Length > 1800 ? sb.ToString().Substring(0,1800) + "\n…(full dump in out/words-" + tag + ".txt)" : sb.ToString());
