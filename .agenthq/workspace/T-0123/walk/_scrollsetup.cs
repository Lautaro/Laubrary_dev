var t = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ChunksMock.Editor.ChunksMockWindow")).FirstOrDefault(x => x != null);
var win = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetType() == t);
if (win == null) return "NO WINDOW";
win.position = new Rect(120f, 120f, 900f, 480f);
t.GetMethod("LoadRecipe", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
    .Invoke(win, new object[] { "Crate Smash" });
win.Focus();
return "resized to " + win.position;
