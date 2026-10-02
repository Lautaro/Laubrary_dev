// Open the LauAssetBrowser popup by calling its own Show() from a probe, since a synthesized press on the
// button does not reach the Clickable that raises it.
var w = ZWin("MirageWindow"); if (w == null) return "no Mirage window";
UnityEngine.UIElements.Button btn = null;
foreach (var e in ZAll(w.rootVisualElement))
{ var b = e as UnityEngine.UIElements.Button; if (b != null && ZDrawn(b) && b.text.Contains("Add Previewable")) { btn = b; break; } }
if (btn == null) return "no Add Previewable button";
var brT = ZType("LauAssetBrowser"); if (brT == null) return "no LauAssetBrowser type";
var pickT = ZType("MirageAssetPicker"); if (pickT == null) return "no MirageAssetPicker type";
var BFs = System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
var findAll = pickT.GetMethod("FindAll", BFs);
var items = findAll.Invoke(null, null) as System.Collections.IEnumerable;
var list = new System.Collections.Generic.List<UnityEngine.Object>();
foreach (var it in items)
{
    var f = it.GetType().GetField("asset");
    if (f != null) list.Add(f.GetValue(it) as UnityEngine.Object);
}
System.Reflection.MethodInfo show = null;
foreach (var m in brT.GetMethods(BFs))
{
    if (m.Name != "Show") continue;
    var pp = m.GetParameters();
    if (pp.Length >= 2 && pp[1].ParameterType != typeof(System.Type) && pp[1].ParameterType != typeof(string)) { show = m; break; }
}
if (show == null) return "no Show";
var ps = show.GetParameters();
var args = new object[ps.Length];
args[0] = btn.worldBound;
args[1] = list;
for (int i = 2; i < ps.Length; i++) args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : (ps[i].ParameterType.IsValueType ? System.Activator.CreateInstance(ps[i].ParameterType) : null);
w.Focus();
show.Invoke(null, args);
var sb = new System.Text.StringBuilder();
sb.Append("Show(").Append(ps.Length).Append(" args) invoked with ").Append(list.Count).Append(" items\n");
foreach (var x in Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
  if (x.GetType().Name.ToLower().Contains("popup")) sb.Append("POPUP ").Append(x.GetType().FullName).Append(" ").Append(x.position).Append("\n");
return sb.ToString();
