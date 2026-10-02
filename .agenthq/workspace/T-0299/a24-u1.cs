var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
UnityEditor.AssetDatabase.DeleteAsset("Assets/Shaper/AuditA24g.asset");
UnityEditor.AssetDatabase.DeleteAsset("Assets/Shaper/AuditA24u.asset");
UnityEditor.Undo.ClearAll();
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.Shaper.Editor.ShaperWindow>()) w0.Close();
UnityEditor.EditorApplication.ExecuteMenuItem("Laubrary/Shaper");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
win.position = new UnityEngine.Rect(50, 50, 2100, 1150); win.Show();
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
System.Func<System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Tree = () =>
{ var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, l); return l; };
System.Func<string, UnityEngine.UIElements.Button> Btn = txt =>
{ foreach (var v in Tree()) if (v is UnityEngine.UIElements.Button b && b.text == txt) return b; return null; };
System.Action<UnityEngine.UIElements.Button> Press = b =>
{ using (var ev = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { ev.target = b; b.SendEvent(ev); } };
Press(Btn("New"));
foreach (var v in Tree()) if (v is UnityEngine.UIElements.TextField tf && tf.value == "New Shaper") tf.value = "AuditA24u";
Press(Btn("Create"));
sb.Append("created AuditA24u\n");
return sb.ToString();
