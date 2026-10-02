var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var pyreT = FT("PyreWindow");
UnityEditor.EditorWindow win = null;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w != null && w.GetType() == pyreT) win = w;
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo curP = null;
for (var t = pyreT; t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
var spec = curP.GetValue(win);
var pfF = pyreT.GetField("frame", BFi);
// the transport button the user presses
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
UnityEngine.UIElements.Button transport = null;
foreach (var v in all) if (v is UnityEngine.UIElements.Button b && b.text != null && (b.text.Contains("Play") || b.text.Contains("Pause"))) transport = b;
sb.Append("transport reads '").Append(transport == null ? "<none>" : transport.text).Append("' tip=").Append(transport == null ? "" : transport.tooltip).Append("\n");
UnityEditor.SessionState.SetString("A25.psamples", "");
double t0 = UnityEditor.EditorApplication.timeSinceStartup, next = t0 + 0.37;
int taken = 0;
UnityEditor.EditorApplication.CallbackFunction sampler = null;
sampler = () => {
    double now = UnityEditor.EditorApplication.timeSinceStartup;
    if (now < next) return;
    next = now + 0.37;
    var cur = UnityEditor.SessionState.GetString("A25.psamples", "");
    UnityEditor.SessionState.SetString("A25.psamples", cur + (now - t0).ToString("F2") + ":f" + pfF.GetValue(win) + " ");
    taken++;
    if (taken >= 12) UnityEditor.EditorApplication.update -= sampler;
};
UnityEditor.EditorApplication.update += sampler;
sb.Append("sampler armed\n");
return sb.ToString();
