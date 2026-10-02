var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
System.Func<System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Tree = () =>
{ var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, l); return l; };
System.Type boxT = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == "ZuiBox") boxT = t;
var openP = boxT.GetProperty("IsOpen", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
var keyF = boxT.GetField("_key", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
int nb = 0;
foreach (var v in Tree()) if (boxT.IsInstanceOfType(v)) { nb++; var k = keyF.GetValue(v); sb.Append("BOX key=").Append(k == null ? "<NULL KEY>" : k.ToString().Substring(0, System.Math.Min(24, k.ToString().Length))).Append(" open=").Append(openP.GetValue(v)).Append("\n"); }
sb.Append("boxes=").Append(nb).Append("\n");
foreach (var v in Tree()) if (v is UnityEngine.UIElements.Button b) sb.Append("BTN [").Append(b.text == null ? "<null>" : b.text).Append("]\n");
return sb.ToString();
