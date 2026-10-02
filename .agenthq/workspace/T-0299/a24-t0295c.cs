var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
System.Func<System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Tree = () =>
{ var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, l); return l; };
System.Func<string, UnityEngine.UIElements.Button> Btn = txt =>
{ foreach (var v in Tree()) if (v is UnityEngine.UIElements.Button b && b.text == txt) return b; return null; };
System.Action<UnityEngine.UIElements.Button> Press = b =>
{ if (b == null) { sb.Append("!! button missing\n"); return; }
  using (var ev = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { ev.target = b; b.SendEvent(ev); } };

System.Type boxT = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == "ZuiBox") boxT = t;
var BF = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var openP = boxT.GetProperty("IsOpen", BF);
var keyF = boxT.GetField("_key", BF);
System.Func<string, string> Short = k => k == null ? "?" : (k.Length > 18 ? k.Substring(0, 18) : k);
System.Func<string> State = () => { var s = new System.Text.StringBuilder(); int o = 0, n = 0;
    foreach (var v in Tree()) if (boxT.IsInstanceOfType(v)) { n++; bool op = (bool)openP.GetValue(v); if (op) o++; s.Append(Short((string)keyF.GetValue(v))).Append('=').Append(op ? 1 : 0).Append("  "); }
    return o + "/" + n + " open :: " + s.ToString(); };

// 1. open every box in BOTH panes
foreach (var v in Tree()) if (boxT.IsInstanceOfType(v)) openP.SetValue(v, true);
sb.Append("1 all open      : ").Append(State()).Append("\n");

// 2. save a view through the real bar
Press(Btn("Save as"));
int tf = 0;
foreach (var v in Tree()) if (v is UnityEngine.UIElements.TextField t2) { t2.value = "A24View"; tf++; }
sb.Append("2 textfields after 'Save as' = ").Append(tf).Append(" | buttons: ");
foreach (var v in Tree()) if (v is UnityEngine.UIElements.Button b) sb.Append('[').Append(b.text).Append(']');
sb.Append("\n");
return sb.ToString();
