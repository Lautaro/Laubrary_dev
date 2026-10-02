// Type a line, then press the window's own ▶ Play and report the transport's state.
var w = ZWin("TextSplashWindow"); if (w == null) return "no window";
var sb = new System.Text.StringBuilder();
string line = UnityEditor.EditorPrefs.GetString("T326.line", "ROUND SIXTEEN");
UnityEngine.UIElements.TextField textField = null;
foreach (var e in ZAll(w.rootVisualElement))
{
    var t = e as UnityEngine.UIElements.TextField;
    if (t == null || !ZDrawn(t)) continue;
    if (ZTip(t).StartsWith("The line to splash")) { textField = t; break; }
}
if (textField != null) { textField.value = line; sb.Append("typed '").Append(textField.value).Append("'\n"); }
else sb.Append("no 'line to splash' field drawn\n");
UnityEngine.UIElements.Button play = null;
foreach (var e in ZAll(w.rootVisualElement)) { var b = e as UnityEngine.UIElements.Button; if (b != null && ZDrawn(b) && (b.text.Contains("Play") || b.text.Contains("Pause"))) play = b; }
if (play == null) return sb.Append("no play button").ToString();
sb.Append("button '").Append(play.text).Append("' at ").Append(play.worldBound).Append("\n");
UnityEngine.UIElements.ScrollView sv = null;
for (var p = play.hierarchy.parent; p != null; p = p.hierarchy.parent) { var s = p as UnityEngine.UIElements.ScrollView; if (s != null) { sv = s; break; } }
if (sv != null && (play.worldBound.yMax > w.position.height || play.worldBound.y < 0))
{ sv.ScrollTo(play); return sb.Append("scrolled into view — run again to press").ToString(); }
sb.Append("pressed -> ").Append(ZClick(play)).Append("\n");
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
foreach (var f in w.GetType().GetFields(BFi))
    if (f.Name == "_scrub" || f.Name == "_playing" || f.Name.Contains("play") || f.Name.Contains("Scrub"))
        sb.Append("   ").Append(f.Name).Append("=").Append(f.GetValue(w)).Append("\n");
sb.Append("button now '").Append(play.text).Append("'\n");
return sb.ToString();
