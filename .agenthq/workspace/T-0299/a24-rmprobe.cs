var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
int removed = 0;
var kill = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
foreach (var c in win.rootVisualElement.Children())
{
    var inner = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>();
    System.Action<UnityEngine.UIElements.VisualElement> W = null;
    W = e => { if (e.tooltip == "probe") inner.Add(e); for (int i = 0; i < e.childCount; i++) W(e[i]); };
    W(c);
    if (inner.Count > 0) kill.Add(c);
}
foreach (var k in kill) { win.rootVisualElement.Remove(k); removed++; }
UnityEditor.SessionState.SetString("A24.t0297host", "");
return "removed probe hosts=" + removed + " dataPath=" + UnityEngine.Application.dataPath;
