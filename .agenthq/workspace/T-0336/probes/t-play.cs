// press the transport button whose text matches T336.want ("Play" or "Pause"); report what it is now.
string want = UnityEditor.EditorPrefs.GetString("T336.want", "Play");
var w = ZWin("ShaperWindow"); if (w == null) return "no window";
UnityEngine.UIElements.Button tb = null;
foreach (var e in ZAll(w.rootVisualElement)) { var b = e as UnityEngine.UIElements.Button; if (b != null && ZDrawn(b) && b.text != null && (b.text.Contains("Play") || b.text.Contains("Pause"))) { tb = b; break; } }
if (tb == null) return "no transport button";
string was = tb.text;
if (!was.Contains(want)) return "already " + (want == "Play" ? "playing" : "paused") + " (button reads '" + was + "')";
string r = ZPress(w, tb);
UnityEditor.EditorPrefs.SetFloat("T336.t0", (float)UnityEditor.EditorApplication.timeSinceStartup);
return "was '" + was + "' -> " + r;
