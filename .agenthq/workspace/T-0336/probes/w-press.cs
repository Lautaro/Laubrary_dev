// Press the nth drawn Button/ToggleButton whose text is T336.pressText, honestly (ZPress).
string bt = UnityEditor.EditorPrefs.GetString("T336.pressText", "");
int nth = UnityEditor.EditorPrefs.GetInt("T336.pressNth", 0);
var w = ZWin("ShaperWindow");
if (w == null) return "no window";
UnityEngine.UIElements.VisualElement hit = null; int seen = 0; var all = new System.Text.StringBuilder();
foreach (var e in ZAll(w.rootVisualElement))
{
    var b = e as UnityEngine.UIElements.Button;
    if (b == null || !ZDrawn(b) || b.text != bt) continue;
    if (seen++ != nth) continue;
    hit = b; break;
}
if (hit == null) return "NO BUTTON '" + bt + "' #" + nth;
return ZPress(w, hit);
