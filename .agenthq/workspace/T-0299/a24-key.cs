var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
win.Focus();
var WT = win.GetType();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo curP = null;
for (var t = WT; t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
System.Reflection.MethodInfo rb = null;
for (var t = WT; t != null; t = t.BaseType) { rb = t.GetMethod("Rebuild", BFi); if (rb != null) break; }
var changeM = WT.GetMethod("Change", BFi);
System.Func<Laubrary.Shaper.ShaperDocument> D = () => curP.GetValue(win) as Laubrary.Shaper.ShaperDocument;
System.Func<string> Sig = () => { var d = D(); return d == null ? "DOC NULL" : ("layer0='" + d.layers[0].name + "' children=" + (d.layers[0].root.children == null ? 0 : d.layers[0].root.children.Count)); };

UnityEditor.Undo.IncrementCurrentGroup();
sb.Append("before edit          : ").Append(Sig()).Append("\n");
changeM.Invoke(win, new object[] { (System.Action)(() => D().layers[0].name = "KeyboardTest") });
rb.Invoke(win, null);
UnityEditor.Undo.IncrementCurrentGroup();
sb.Append("after edit           : ").Append(Sig()).Append("\n");

// Ctrl+Z / Ctrl+Y are bound to Edit/Undo and Edit/Redo; drive the SAME commands the keystrokes fire
bool okU = UnityEditor.EditorApplication.ExecuteMenuItem("Edit/Undo");
rb.Invoke(win, null);
sb.Append("Edit/Undo executed=").Append(okU).Append("  -> ").Append(Sig()).Append("\n");
bool okR = UnityEditor.EditorApplication.ExecuteMenuItem("Edit/Redo");
rb.Invoke(win, null);
sb.Append("Edit/Redo executed=").Append(okR).Append("  -> ").Append(Sig()).Append("\n");
UnityEditor.EditorApplication.ExecuteMenuItem("Edit/Undo");
rb.Invoke(win, null);
sb.Append("Edit/Undo again      : ").Append(Sig()).Append("\n");

// does the window swallow a raw ctrl+Z KeyDown before the editor's own Undo sees it?
bool swallowed = false;
char nul = (char)0;
using (var ke = UnityEngine.UIElements.KeyDownEvent.GetPooled(nul, UnityEngine.KeyCode.Z, UnityEngine.EventModifiers.Control))
{
    ke.target = win.rootVisualElement;
    win.rootVisualElement.SendEvent(ke);
    swallowed = ke.isPropagationStopped;
}
sb.Append("raw ctrl+Z KeyDown stopped by the window = ").Append(swallowed).Append(" (false = the editor's own Undo still receives it)\n");
sb.Append("doc alive after all of it = ").Append(D() != null).Append("\n");
return sb.ToString();
