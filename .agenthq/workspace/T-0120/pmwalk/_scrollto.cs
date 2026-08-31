var wt = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ChunksMock.Editor.ChunksMockWindow")).FirstOrDefault(x => x != null);
var win = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetType() == wt);
UnityEngine.UIElements.ScrollView sv = null;
System.Action<UnityEngine.UIElements.VisualElement> f = null;
f = v => { if (sv == null && v is UnityEngine.UIElements.ScrollView s) sv = s; foreach (var c in v.hierarchy.Children()) f(c); };
f(win.rootVisualElement);
sv.scrollOffset = new Vector2(0f, 200f);
win.Repaint();
return "set offset=" + sv.scrollOffset;
