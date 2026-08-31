var wt = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ChunksMock.Editor.ChunksMockWindow")).FirstOrDefault(x => x != null);
var win = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetType() == wt);
win.position = new Rect(120f, 120f, 1000f, 720f); win.Repaint(); return "restored";
