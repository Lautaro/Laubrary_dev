var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
UnityEditor.AssetDatabase.DeleteAsset("Assets/Shaper/AuditA24u.asset");
UnityEditor.Undo.ClearAll();
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.Shaper.Editor.ShaperWindow>()) w0.Close();
UnityEditor.EditorApplication.ExecuteMenuItem("Laubrary/Shaper");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
win.position = new UnityEngine.Rect(50, 50, 2100, 1150); win.Show();
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
System.Func<string, UnityEngine.UIElements.Button> Btn = txt =>
{ foreach (var v in Tree()) if (v is UnityEngine.UIElements.Button b && b.text == txt) return b; return null; };
System.Action<UnityEngine.UIElements.Button> Press = b =>
{ using (var ev = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { ev.target = b; b.SendEvent(ev); } };

sb.Append("group at start        = ").Append(UnityEditor.Undo.GetCurrentGroup()).Append("\n");
Press(Btn("New"));
foreach (var v in Tree()) if (v is UnityEngine.UIElements.TextField tf && tf.value == "New Shaper") tf.value = "AuditA24v";
Press(Btn("Create"));
var doc = curP.GetValue(win) as Laubrary.Shaper.ShaperDocument;
sb.Append("group after Create    = ").Append(UnityEditor.Undo.GetCurrentGroup()).Append(" name='").Append(UnityEditor.Undo.GetCurrentGroupName()).Append("'\n");

// what a real user event does between two actions
UnityEditor.Undo.IncrementCurrentGroup();
sb.Append("group after Increment = ").Append(UnityEditor.Undo.GetCurrentGroup()).Append("\n");

var changeM = WT.GetMethod("Change", BFi);
changeM.Invoke(win, new object[] { (System.Action)(() => doc.layers[0].name = "EditOne") });
rb.Invoke(win, null);
sb.Append("group after edit      = ").Append(UnityEditor.Undo.GetCurrentGroup()).Append(" name='").Append(UnityEditor.Undo.GetCurrentGroupName()).Append("' layerName=").Append(doc.layers[0].name).Append("\n");

UnityEditor.Undo.IncrementCurrentGroup();
UnityEditor.Undo.PerformUndo(); rb.Invoke(win, null);
var d2 = curP.GetValue(win) as Laubrary.Shaper.ShaperDocument;
sb.Append("after 1 undo          = ").Append(d2 == null ? "windowDoc NULL" : ("alive layerName=" + (d2.layers.Count > 0 ? d2.layers[0].name : "<none>")))
  .Append(" loadable=").Append(UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>("Assets/Shaper/AuditA24v.asset") != null).Append("\n");
UnityEditor.Undo.PerformRedo(); rb.Invoke(win, null);
var d3 = curP.GetValue(win) as Laubrary.Shaper.ShaperDocument;
sb.Append("after redo            = ").Append(d3 == null ? "windowDoc NULL" : ("alive layerName=" + (d3.layers.Count > 0 ? d3.layers[0].name : "<none>"))).Append("\n");
return sb.ToString();
