var t = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ChunksMock.Editor.ChunksMockWindow")).FirstOrDefault(x => x != null);
var win = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetType() == t);
if (win == null) return "NO WINDOW";
UnityEngine.UIElements.ScrollView sv = null;
System.Action<UnityEngine.UIElements.VisualElement> f = null;
f = v => { if (sv == null && v is UnityEngine.UIElements.ScrollView s) sv = s; foreach (var c in v.hierarchy.Children()) f(c); };
f(win.rootVisualElement);
if (sv == null) return "NO SCROLLVIEW";
UnityEngine.UIElements.Button addLayer = null;
System.Action<UnityEngine.UIElements.VisualElement> g = null;
g = v => { var b = v as UnityEngine.UIElements.Button; if (b != null && b.text == "Add layer") addLayer = b; foreach (var c in v.hierarchy.Children()) g(c); };
g(win.rootVisualElement);
return "offset=" + sv.scrollOffset + " contentH=" + sv.contentContainer.worldBound.height.ToString("0.0")
    + " viewportH=" + sv.worldBound.height.ToString("0.0")
    + " AddLayer=" + (addLayer == null ? "none" : addLayer.worldBound.ToString());
