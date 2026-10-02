var w = ZWin(UnityEditor.EditorPrefs.GetString("T334.walkWin","ShaperWindow")); if (w == null) return "no window";
float ww = UnityEditor.EditorPrefs.GetFloat("T334.w", 820f), hh = UnityEditor.EditorPrefs.GetFloat("T334.h", 520f);
var p = w.position; w.position = new UnityEngine.Rect(p.x, p.y, ww, hh); w.Repaint();
return "asked " + ww + "x" + hh + " -> " + w.position + " min=" + w.minSize;
