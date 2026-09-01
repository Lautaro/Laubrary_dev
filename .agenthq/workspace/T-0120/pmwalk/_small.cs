var wt = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ChunksMock.Editor.ChunksMockWindow")).FirstOrDefault(x => x != null);
var win = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetType() == wt);
if (win == null) return "NO WINDOW";
win.position = new Rect(120f, 120f, 900f, 480f);
win.Repaint();
return "resized to " + win.position;
