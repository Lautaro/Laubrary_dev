float y = UnityEditor.EditorPrefs.GetFloat("T334.scrollY", 0f);
string wn = UnityEditor.EditorPrefs.GetString("T334.walkWin", "ChunkWindow");
var win = ZWin(wn); if (win == null) return "no window";
UnityEngine.UIElements.ScrollView sv = null;
foreach (var e in ZAll(win.rootVisualElement)) { var s = e as UnityEngine.UIElements.ScrollView; if (s != null && ZDrawn(s)) { sv = s; break; } }
if (sv == null) return "no scrollview";
sv.scrollOffset = new Vector2(sv.scrollOffset.x, y);
win.Repaint();
return "offset=" + sv.scrollOffset;
