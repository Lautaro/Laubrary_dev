var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;

System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
System.Func<System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Tree = () =>
{ var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, l); return l; };
System.Func<string, UnityEngine.UIElements.Button> Btn = txt =>
{ foreach (var v in Tree()) if (v is UnityEngine.UIElements.Button b && b.text == txt) return b; return null; };
System.Action<UnityEngine.UIElements.Button> Press = b =>
{ using (var ev = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { ev.target = b; b.SendEvent(ev); } };

// step 1: press New (the affordance a user sees in the empty state)
var nb = Btn("New");
sb.Append("New found=").Append(nb != null).Append("\n");
Press(nb);
// what appeared?
var after = Tree();
var fields = new System.Text.StringBuilder();
foreach (var v in after)
    if (v is UnityEngine.UIElements.TextField tf) fields.Append("[TextField value='").Append(tf.value).Append("' label='").Append(tf.label).Append("'] ");
sb.Append("after New: ").Append(fields.ToString()).Append("\n");
var btxt = new System.Text.StringBuilder();
foreach (var v in after) if (v is UnityEngine.UIElements.Button b) btxt.Append(b.text).Append(" | ");
sb.Append("buttons now: ").Append(btxt.ToString()).Append("\n");

// step 2: type a name and press Create
foreach (var v in after) if (v is UnityEngine.UIElements.TextField tf) { tf.value = "AuditA25b"; }
var cb = Btn("Create");
sb.Append("Create found=").Append(cb != null).Append("\n");
if (cb != null) Press(cb);

System.Reflection.PropertyInfo curP = null;
for (var t = win.GetType(); t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
var doc = curP.GetValue(win) as Laubrary.Shaper.ShaperDocument;
sb.Append("bound=").Append(doc == null ? "<none>" : doc.name).Append(" path=").Append(doc == null ? "" : UnityEditor.AssetDatabase.GetAssetPath(doc)).Append("\n");
if (doc != null)
{
    sb.Append("layers=").Append(doc.layers.Count).Append(" frames=").Append(doc.frameCount).Append(" fps=").Append(doc.frameRate)
      .Append(" canvas=").Append(doc.canvasWidth).Append("x").Append(doc.canvasHeight)
      .Append(" lights=").Append(doc.lightRig == null ? 0 : doc.lightRig.lights.Count).Append("\n");
    var px = Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(doc, 0);
    int lit = 0; foreach (var c in px) if (c.a > 0) lit++;
    sb.Append("lit pixels at frame 0 = ").Append(lit).Append("\n");
    sb.Append("dirtyOnCreate=").Append(UnityEditor.EditorUtility.IsDirty(doc)).Append("\n");
}
return sb.ToString();
