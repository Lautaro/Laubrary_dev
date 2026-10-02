var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var pyreT = FT("PyreWindow");
UnityEditor.EditorWindow win = null;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w != null && w.GetType() == pyreT) win = w;
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo curP = null;
for (var t = pyreT; t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
var spec = curP.GetValue(win) as UnityEngine.Object;
sb.Append("bound=").Append(spec == null ? "<none>" : UnityEditor.AssetDatabase.GetAssetPath(spec)).Append("\n");
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
UnityEngine.UIElements.Button bake = null;
foreach (var v in all) if (v is UnityEngine.UIElements.Button b && b.text == "Bake") bake = b;
sb.Append("Bake button found=").Append(bake != null).Append(" enabled=").Append(bake == null ? false : bake.enabledInHierarchy).Append(" tip=").Append(bake == null ? "" : bake.tooltip).Append("\n");
if (bake != null) using (var ev = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { ev.target = bake; bake.SendEvent(ev); }
UnityEditor.AssetDatabase.Refresh();
foreach (var p in System.IO.Directory.GetFiles(UnityEngine.Application.dataPath + "/Pyre/AuditA25"))
    sb.Append("  FILE ").Append(System.IO.Path.GetFileName(p)).Append(" ").Append(new System.IO.FileInfo(p).Length).Append(" bytes\n");
return sb.ToString();
