var win = ZWin(UnityEditor.EditorPrefs.GetString("T320.capWin","PyreWindow")); if (win==null) return "no window";
float want = float.Parse(UnityEditor.EditorPrefs.GetString("T320.scroll","0"));
UnityEngine.UIElements.ScrollView sv = null;
foreach (var e in ZAll(win.rootVisualElement)) if (e is UnityEngine.UIElements.ScrollView s2 && ZDrawn(e)) { if (sv == null || e.worldBound.height > sv.worldBound.height) sv = s2; }
if (sv == null) return "no scrollview";
sv.scrollOffset = new UnityEngine.Vector2(0, want);
win.Repaint();
return "scrolled to " + sv.scrollOffset + " range " + sv.verticalScroller.highValue + " sv=" + sv.worldBound;
