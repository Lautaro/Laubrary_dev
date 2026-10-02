var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
float ww = UnityEditor.EditorPrefs.GetFloat("A25.sw", 820f), hh = UnityEditor.EditorPrefs.GetFloat("A25.sh", 520f);
win.position = new UnityEngine.Rect(60, 60, ww, hh);
win.Focus(); win.Repaint();
return "shaper set to " + win.position.width + "x" + win.position.height;
