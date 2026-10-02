var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
UnityEditor.AssetDatabase.DeleteAsset("Assets/Shaper/AuditA24a.asset");
UnityEditor.Undo.ClearAll();

// a FRESH document through the window's own New -> name -> Create
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
{ if (b == null) { sb.Append("!! MISSING\n"); return; } using (var ev = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { ev.target = b; b.SendEvent(ev); } };

Press(Btn("New"));
foreach (var v in Tree()) if (v is UnityEngine.UIElements.TextField tf && tf.value == "New Shaper") tf.value = "AuditA24g";
Press(Btn("Create"));
var doc = curP.GetValue(win) as Laubrary.Shaper.ShaperDocument;
sb.Append("created=").Append(doc == null ? "<none>" : doc.name).Append(" path=").Append(doc == null ? "" : UnityEditor.AssetDatabase.GetAssetPath(doc)).Append("\n");

// EXACTLY TWO edits through the window's own undoable wrapper
var changeM = WT.GetMethod("Change", BFi);
changeM.Invoke(win, new object[] { (System.Action)(() => doc.layers[0].name = "Edit one") });
changeM.Invoke(win, new object[] { (System.Action)(() => doc.layers[0].name = "Edit two") });
rb.Invoke(win, null);
sb.Append("after 2 edits: layerName=").Append(doc.layers[0].name).Append("\n");

// now press undo FIVE times - three more than the edits made
for (int i = 1; i <= 5; i++)
{
    UnityEditor.Undo.PerformUndo();
    var d = curP.GetValue(win) as Laubrary.Shaper.ShaperDocument;
    bool alive = d != null;
    string ln = "?";
    try { ln = alive && d.layers != null && d.layers.Count > 0 ? d.layers[0].name : "<no layers>"; } catch (System.Exception) { ln = "<threw>"; }
    var onDisk = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>("Assets/Shaper/AuditA24g.asset");
    sb.Append("undo #").Append(i).Append(": windowDoc=").Append(alive ? "alive" : "NULL")
      .Append(" layerName=").Append(ln)
      .Append(" loadableFromDisk=").Append(onDisk != null)
      .Append(" fileExists=").Append(System.IO.File.Exists(System.IO.Path.Combine(System.IO.Directory.GetParent(UnityEngine.Application.dataPath).FullName, "Assets/Shaper/AuditA24g.asset")))
      .Append("\n");
}
return sb.ToString();
