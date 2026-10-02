var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var WT = win.GetType();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo curP = null;
for (var t = WT; t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
System.Reflection.MethodInfo rb = null;
for (var t = WT; t != null; t = t.BaseType) { rb = t.GetMethod("Rebuild", BFi); if (rb != null) break; }

System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
System.Func<System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Tree = () =>
{ var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, l); return l; };
System.Func<string, System.Collections.Generic.List<UnityEngine.UIElements.Button>> Btns = txt =>
{ var l = new System.Collections.Generic.List<UnityEngine.UIElements.Button>();
  foreach (var v in Tree()) if (v is UnityEngine.UIElements.Button b && b.text == txt) l.Add(b); return l; };
System.Action<UnityEngine.UIElements.Button> Press = b =>
{ if (b == null) { sb.Append("!! MISSING BUTTON\n"); return; }
  using (var ev = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { ev.target = b; b.SendEvent(ev); } };

System.Func<string> Sig = () => {
    var d = curP.GetValue(win) as Laubrary.Shaper.ShaperDocument;
    if (d == null) return "<no doc>";
    if (d.layers == null || d.layers.Count == 0) return "<no layers>";
    var n = d.layers[0].root;
    if (n == null) return "<no root>";
    var s = new System.Text.StringBuilder();
    s.Append("kind=").Append(n.kind).Append(" children=").Append(n.children == null ? 0 : n.children.Count).Append(" [");
    if (n.children != null) foreach (var c in n.children) s.Append(c.name).Append('/').Append(c.mode).Append(' ');
    s.Append("]");
    return s.ToString();
};

try
{
    sb.Append("A start                  : ").Append(Sig()).Append("\n");

    var add = Btns("+ Add member");
    sb.Append("   '+ Add member' count=").Append(add.Count).Append("\n");
    Press(add.Count > 0 ? add[0] : null);
    sb.Append("B after +Add member      : ").Append(Sig()).Append("\n");

    UnityEditor.Undo.PerformUndo(); rb.Invoke(win, null);
    sb.Append("B undo                   : ").Append(Sig()).Append("\n");
    UnityEditor.Undo.PerformRedo(); rb.Invoke(win, null);
    sb.Append("B redo                   : ").Append(Sig()).Append("\n");
    UnityEditor.Undo.PerformUndo(); rb.Invoke(win, null);
    sb.Append("B undo again             : ").Append(Sig()).Append("\n");

    var xs = Btns("×");
    sb.Append("   '×' count=").Append(xs.Count).Append("\n");
    if (xs.Count > 0)
    {
        Press(xs[xs.Count - 1]);
        rb.Invoke(win, null);
        sb.Append("C after × (remove)       : ").Append(Sig()).Append("\n");
        UnityEditor.Undo.PerformUndo(); rb.Invoke(win, null);
        sb.Append("C undo                   : ").Append(Sig()).Append("\n");
    }
}
catch (System.Exception ex)
{
    sb.Append("EXCEPTION ").Append(ex.GetType().Name).Append(": ").Append(ex.Message).Append("\n");
    sb.Append(ex.StackTrace == null ? "" : ex.StackTrace).Append("\n");
}
return sb.ToString();
