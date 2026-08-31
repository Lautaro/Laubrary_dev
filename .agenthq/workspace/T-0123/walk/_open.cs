var t = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ChunksMock.Editor.ChunksMockWindow")).FirstOrDefault(x => x != null);
if (t == null) return "NO TYPE";
foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>().Where(w => w.GetType() == t).ToArray()) w.Close();
// Put the divider back where the code asks for it, so this really is a first-run layout (see P5).
EditorPrefs.SetFloat("ZUI.Split.chunks.mock.prototype.split.v2", 480f);
EditorApplication.ExecuteMenuItem("Laubrary/Chunks Mock (Prototype)");
var win = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetType() == t);
if (win == null) return "NO WINDOW";
win.position = new Rect(120f, 120f, 1000f, 720f);
win.Focus();
return "opened at " + win.position;
