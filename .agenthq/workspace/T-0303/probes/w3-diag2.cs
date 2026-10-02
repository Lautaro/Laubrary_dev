var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var WT = win.GetType();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo curP = null;
for (var t = WT; t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
var doc = curP.GetValue(win) as Laubrary.Shaper.ShaperDocument;
sb.Append("doc=").Append(doc.name).Append(" layers=").Append(doc.layers.Count).Append("\n");
var node = doc.layers[0].root;
sb.Append("root kind=").Append(node.kind).Append(" enabled=").Append(node.enabled);
if (node.kind == Laubrary.Shaper.ShaperNodeKind.Primitive) sb.Append(" primitive=").Append(node.primitive.kind);
sb.Append("\n");
sb.Append("swarm: enabled=").Append(node.swarm.enabled).Append(" count=").Append(node.swarm.count).Append(" shape=").Append(node.swarm.shape).Append(" timing=").Append(node.swarm.timing).Append("\n");
var swF = WT.GetField("swarmSection", BFi);
var sec = swF.GetValue(win);
sb.Append("swarmSection null=").Append(sec == null).Append("\n");
if (sec != null)
{
    var ve = sec as UnityEngine.UIElements.VisualElement;
    sb.Append("  section childCount=").Append(ve.childCount).Append(" parent=").Append(ve.parent == null ? "<detached>" : ve.parent.GetType().Name).Append("\n");
    var op = sec.GetType().GetProperty("IsOpen");
    sb.Append("  IsOpen=").Append(op == null ? "<noprop>" : op.GetValue(sec).ToString()).Append("\n");
    foreach (var f in sec.GetType().GetFields(BFi))
        if (typeof(UnityEngine.UIElements.VisualElement).IsAssignableFrom(f.FieldType))
        { var c = f.GetValue(sec) as UnityEngine.UIElements.VisualElement; sb.Append("  field ").Append(f.Name).Append(" children=").Append(c == null ? -1 : c.childCount).Append("\n"); }
}
return sb.ToString();
