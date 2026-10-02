var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var WT = win.GetType();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo curP = null;
for (var t = WT; t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
var doc = curP.GetValue(win) as Laubrary.Shaper.ShaperDocument;
sb.Append("doc=").Append(doc.name).Append(" layers=").Append(doc.layers.Count).Append("\n");

// turn the root into a Bag through the window's own undoable Change() wrapper (what the shape picker calls)
var changeM = WT.GetMethod("Change", BFi);
sb.Append("Change method=").Append(changeM == null ? "NULL" : changeM.ToString()).Append("\n");
var node = doc.layers[0].root;
sb.Append("node kind before=").Append(node.kind).Append("\n");
changeM.Invoke(win, new object[] { (System.Action)(() => {
    node.kind = Laubrary.Shaper.ShaperNodeKind.Bag;
    if (node.children == null) node.children = new System.Collections.Generic.List<Laubrary.Shaper.ShaperNode>();
}) });
System.Reflection.MethodInfo rb = null;
for (var t = WT; t != null; t = t.BaseType) { rb = t.GetMethod("Rebuild", BFi); if (rb != null) break; }
rb.Invoke(win, null);
sb.Append("node kind after=").Append(node.kind).Append(" children=").Append(node.children.Count).Append("\n");

System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
var b = new System.Text.StringBuilder();
foreach (var v in all) if (v is UnityEngine.UIElements.Button bb) b.Append('[').Append(bb.text).Append(']');
sb.Append("buttons: ").Append(b.ToString()).Append("\n");
return sb.ToString();
