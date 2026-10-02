var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var WT = win.GetType();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo curP = null;
for (var t = WT; t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
System.Reflection.MethodInfo rb = null;
for (var t = WT; t != null; t = t.BaseType) { rb = t.GetMethod("Rebuild", BFi); if (rb != null) break; }
var changeM = WT.GetMethod("Change", BFi);
System.Func<Laubrary.Shaper.ShaperDocument> D = () => curP.GetValue(win) as Laubrary.Shaper.ShaperDocument;

System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
System.Func<System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Tree = () =>
{ var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, l); return l; };
System.Func<string, System.Collections.Generic.List<UnityEngine.UIElements.Button>> Btns = txt =>
{ var l = new System.Collections.Generic.List<UnityEngine.UIElements.Button>();
  foreach (var v in Tree()) if (v is UnityEngine.UIElements.Button b && b.text == txt) l.Add(b); return l; };
System.Action<UnityEngine.UIElements.Button> Press = b =>
{ if (b == null) { sb.Append("  !! MISSING BUTTON\n"); return; }
  using (var ev = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { ev.target = b; b.SendEvent(ev); } };

// make it a Bag with two members (setup, not under test)
var n0 = D().layers[0].root;
changeM.Invoke(win, new object[] { (System.Action)(() => {
    n0.kind = Laubrary.Shaper.ShaperNodeKind.Bag;
    if (n0.children == null) n0.children = new System.Collections.Generic.List<Laubrary.Shaper.ShaperNode>();
}) });
rb.Invoke(win, null);
UnityEditor.Undo.IncrementCurrentGroup();
Press(Btns("+ Add member").Count > 0 ? Btns("+ Add member")[0] : null);
UnityEditor.Undo.IncrementCurrentGroup();
Press(Btns("+ Add member").Count > 0 ? Btns("+ Add member")[0] : null);
UnityEditor.Undo.IncrementCurrentGroup();
rb.Invoke(win, null);

System.Func<string> Sig = () => {
    var d = D(); if (d == null) return "DOC NULL";
    var n = d.layers[0].root; var s = new System.Text.StringBuilder();
    s.Append(n.children == null ? 0 : n.children.Count).Append(" [");
    if (n.children != null) foreach (var c in n.children) s.Append(c.name).Append('/').Append(c.mode).Append(' ');
    return s.Append(']').ToString(); };

// each edit type: do it, undo it, redo it, undo it again (leaving state as before)
System.Action<string, System.Action> Case = (label, act) =>
{
    UnityEditor.Undo.IncrementCurrentGroup();
    string before = Sig();
    act();
    rb.Invoke(win, null);
    string after = Sig();
    UnityEditor.Undo.IncrementCurrentGroup();
    UnityEditor.Undo.PerformUndo(); rb.Invoke(win, null);
    string undone = Sig();
    UnityEditor.Undo.PerformRedo(); rb.Invoke(win, null);
    string redone = Sig();
    UnityEditor.Undo.PerformUndo(); rb.Invoke(win, null);
    sb.Append(label).Append("\n   before=").Append(before).Append("\n   after =").Append(after)
      .Append("\n   undo  =").Append(undone).Append(" -> UNDO OK=").Append(undone == before)
      .Append("\n   redo  =").Append(redone).Append(" -> REDO OK=").Append(redone == after).Append("\n");
};

sb.Append("SETUP: ").Append(Sig()).Append("\n\n");
Case("1. + Add member", () => { var l = Btns("+ Add member"); Press(l.Count > 0 ? l[0] : null); });
Case("2. change a member's combine mode", () => changeM.Invoke(win, new object[] { (System.Action)(() => D().layers[0].root.children[1].mode = Laubrary.Shaper.ShaperCombineMode.Subtract) }));
Case("3. reorder members", () => changeM.Invoke(win, new object[] { (System.Action)(() => {
    var ch = D().layers[0].root.children; var m = ch[0]; ch.RemoveAt(0); ch.Insert(1, m); }) }));
Case("4. rename a member", () => changeM.Invoke(win, new object[] { (System.Action)(() => D().layers[0].root.children[0].name = "Renamed") }));
Case("5. remove a member (row ×)", () => { var xs = Btns("×"); Press(xs.Count > 0 ? xs[xs.Count - 1] : null); });
return sb.ToString();
