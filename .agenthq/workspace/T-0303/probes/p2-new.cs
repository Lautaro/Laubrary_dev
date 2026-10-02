var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var pyreT = FT("PyreWindow");
UnityEditor.EditorWindow win = null;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w != null && w.GetType() == pyreT) win = w;
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
System.Func<System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Tree = () => { var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, l); return l; };
System.Func<string, UnityEngine.UIElements.Button> Btn = t => { foreach (var v in Tree()) if (v is UnityEngine.UIElements.Button b && b.text == t) return b; return null; };
System.Action<UnityEngine.UIElements.Button> Press = b => { using (var ev = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { ev.target = b; b.SendEvent(ev); } };
var nb = Btn("New"); sb.Append("New found=").Append(nb != null).Append("\n"); Press(nb);
foreach (var v in Tree()) if (v is UnityEngine.UIElements.TextField tf) { sb.Append("name field default='").Append(tf.value).Append("'\n"); tf.value = "AuditA25Pyre"; }
var cb = Btn("Create"); sb.Append("Create found=").Append(cb != null).Append("\n"); if (cb != null) Press(cb);
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo curP = null;
for (var t = pyreT; t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
var cur = curP.GetValue(win) as UnityEngine.Object;
sb.Append("bound=").Append(cur == null ? "<none>" : cur.name).Append(" path=").Append(cur == null ? "" : UnityEditor.AssetDatabase.GetAssetPath(cur)).Append(" dirty=").Append(cur == null ? false : UnityEditor.EditorUtility.IsDirty(cur)).Append("\n");
if (cur != null)
{
    var t2 = cur.GetType();
    foreach (var f in t2.GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public))
        if (f.Name == "frames" || f.Name == "fps" || f.Name == "width" || f.Name == "height" || f.Name == "layers" || f.Name == "canvasWidth" || f.Name == "canvasHeight")
        { var v2 = f.GetValue(cur); sb.Append("  ").Append(f.Name).Append("=").Append(v2 is System.Collections.ICollection c ? c.Count.ToString() : (v2 == null ? "null" : v2.ToString())).Append("\n"); }
}
int nb2 = 0; foreach (var v in Tree()) if (v is UnityEngine.UIElements.Button) nb2++;
sb.Append("buttons now=").Append(nb2).Append("\n");
return sb.ToString();
