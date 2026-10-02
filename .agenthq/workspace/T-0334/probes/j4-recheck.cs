var sb = new System.Text.StringBuilder();
// (a) Tapestry's Play button width
var tw = ZWin("TapestryWindow");
foreach (var e in ZAll(tw.rootVisualElement))
{ var b = e as UnityEngine.UIElements.Button; if (b != null && ZDrawn(b) && b.text != null && b.text.Contains("Play"))
    sb.Append("Tapestry '").Append(b.text).Append("' w=").Append(b.worldBound.width.ToString("F1")).Append("\n"); }
// (b) Pyre library cells: elided, and any two showing the same string
var pw = ZWin("PyreWindow");
var browse = ZFindBtn(pw, "Browse", 0);
if (browse != null) ZClick(browse);
return sb.ToString();
