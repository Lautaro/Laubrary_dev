var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;

// RE-ENTRY: close every instance, reopen from the menu
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.Shaper.Editor.ShaperWindow>()) w0.Close();
UnityEditor.EditorApplication.ExecuteMenuItem("Laubrary/Shaper");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
win.position = new UnityEngine.Rect(60, 60, 1600, 1150);
win.Show();
System.Reflection.PropertyInfo curP = null;
for (var t = win.GetType(); t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
var d = curP.GetValue(win) as Laubrary.Shaper.ShaperDocument;
sb.Append("RE-ENTRY: bound=").Append(d == null ? "<empty state>" : d.name).Append(" title=").Append(win.titleContent.text).Append("\n");

System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
System.Func<System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Tree = () =>
{ var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, l); return l; };
System.Func<string, UnityEngine.UIElements.Button> Btn = txt =>
{ foreach (var v in Tree()) if (v is UnityEngine.UIElements.Button b && b.text == txt) return b; return null; };
System.Action<UnityEngine.UIElements.Button> Press = b =>
{ using (var ev = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { ev.target = b; b.SendEvent(ev); } };

int n = 0, notip = 0;
foreach (var v in Tree()) if (v is UnityEngine.UIElements.Button b) { n++; if (string.IsNullOrEmpty(b.tooltip)) notip++; }
sb.Append("empty state buttons=").Append(n).Append(" withoutTooltip=").Append(notip).Append(" elements=").Append(Tree().Count).Append("\n");

// SECOND New in the same session
Press(Btn("New"));
foreach (var v in Tree()) if (v is UnityEngine.UIElements.TextField tf) tf.value = "AuditA24b";
Press(Btn("Create"));
var d2 = curP.GetValue(win) as Laubrary.Shaper.ShaperDocument;
sb.Append("SECOND New: bound=").Append(d2 == null ? "<none>" : d2.name).Append(" path=").Append(d2 == null ? "" : UnityEditor.AssetDatabase.GetAssetPath(d2)).Append("\n");
if (d2 != null)
{
    var px = Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(d2, 0);
    int lit = 0; foreach (var c in px) if (c.a > 0) lit++;
    sb.Append("layers=").Append(d2.layers.Count).Append(" frames=").Append(d2.frameCount).Append(" lit@0=").Append(lit).Append(" dirty=").Append(UnityEditor.EditorUtility.IsDirty(d2)).Append("\n");
}
return sb.ToString();
