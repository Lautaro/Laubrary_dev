// ZClick(e): press an element the way a mouse does — PointerDown then PointerUp at its own centre, with a
// real pointer id, which is what a Clickable manipulator listens for.  Returns false if it could not.
var win = ZWin("ShaperWindow");
var sb = new System.Text.StringBuilder();
foreach (var name in new string[]{ "AnimationClip", "ShaperClip", "GIF" }) {
  foreach (var e in ZAll(win.rootVisualElement)) {
    if (!ZDrawn(e)) continue; var b = e as UnityEngine.UIElements.Button; if (b == null || b.text != name) continue;
    bool on = b.ClassListContains("zui-togglebutton--on");
    if (!on) { ZClick(b); sb.Append(name).Append(":turnedOn "); } else sb.Append(name).Append(":alreadyOn ");
    break;
  }
}
return sb.ToString();
