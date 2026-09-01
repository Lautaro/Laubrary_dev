var t = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ChunksMock.Editor.ChunksMockWindow")).FirstOrDefault(x => x != null);
var win = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetType() == t);
if (win == null) return "NO WINDOW";
// Precondition only: a long stack, so the left pane actually scrolls. The TEST is the real mouse click.
UnityEngine.UIElements.Button addLayer = null;
System.Action<UnityEngine.UIElements.VisualElement> g = null;
g = v => { var b = v as UnityEngine.UIElements.Button; if (b != null && b.text == "Add layer") addLayer = b; foreach (var c in v.hierarchy.Children()) g(c); };
for (int i = 0; i < 8; i++)
{
    addLayer = null;
    g(win.rootVisualElement);
    if (addLayer == null) return "no Add layer button at i=" + i;
    using (var e = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled())
    {
        e.target = addLayer;
        addLayer.SendEvent(e);
    }
}
return "added 8 layers";
