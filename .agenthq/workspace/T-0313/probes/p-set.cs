// T-0313 — Pyre setup, one scratch spec with ONE layer whose card content is chosen by prefs:
//   T313.form   short type name of a PyreForm ("TorchForm", …) or "-" for no form (a raw ShapeForm layer)
//   T313.shape  a ShapeForm enum name ("Disc", "Star", …) — only meaningful when T313.form is "-"
//   T313.pane   the dial pane's width. PyreWindow's pane is the serialized field `leftPaneWidth`, NOT a
//               Z.Split pref, and it drives Z.ColumnFlow(360): <720 = 1 col, >=720 = 2, >=1440 = 4, and it
//               is clamped to min(1458, window.width - 260), so 4 columns need a window of ~1720+.
//   T313.winw   window width
// Scratch asset: Assets/Pyre/AuditT0313.asset — deleted with its .meta at the end of the task.
var sb = new System.Text.StringBuilder();
string formName = UnityEditor.EditorPrefs.GetString("T313.form", "-");
string shapeName = UnityEditor.EditorPrefs.GetString("T313.shape", "Disc");
float pane = float.Parse(UnityEditor.EditorPrefs.GetString("T313.pane", "360"));
float winw = float.Parse(UnityEditor.EditorPrefs.GetString("T313.winw", "1700"));
const string path = "Assets/Pyre/AuditT0313.asset";

var spec = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Pyre.Pyre>(path);
if (spec == null)
{
    spec = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Pyre.Pyre>();
    spec.layers.Clear();
    spec.layers.Add(new Laubrary.Pyre.PyreLayer { name = "Card", matteEnabled = false });
    UnityEditor.AssetDatabase.CreateAsset(spec, path);
    sb.Append("created ").Append(path).Append("\n");
}
var layer = spec.layers[0];
if (formName == "-")
{
    layer.form = null;
    layer.shapeForm = (Laubrary.Pyre.ShapeForm)System.Enum.Parse(typeof(Laubrary.Pyre.ShapeForm), shapeName);
}
else
{
    var ft = ZType(formName);
    if (ft == null) return "NO FORM TYPE " + formName;
    layer.form = (Laubrary.Pyre.PyreForm)System.Activator.CreateInstance(ft);
}
spec.previewLayerSel = 0;

var win = ZWin("PyreWindow");
if (win == null) { UnityEditor.EditorApplication.ExecuteMenuItem("Laubrary/Pyre"); win = ZWin("PyreWindow"); }
if (win == null) return "NO PYRE WINDOW";
win.position = new UnityEngine.Rect(5, 20, winw, 1000);
win.Show();

var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
var wt = win.GetType();
var lpw = wt.GetField("leftPaneWidth", BFi);
if (lpw != null) lpw.SetValue(win, pane);

System.Reflection.MethodInfo setAsset = null;
for (var t = wt; t != null && setAsset == null; t = t.BaseType)
    setAsset = t.GetMethod("SetAsset", BFi | System.Reflection.BindingFlags.DeclaredOnly);
setAsset.Invoke(win, new object[] { spec });
System.Reflection.MethodInfo rebuild = null;
for (var t = wt; t != null && rebuild == null; t = t.BaseType)
    rebuild = t.GetMethod("Rebuild", BFi | System.Reflection.BindingFlags.DeclaredOnly);
rebuild.Invoke(win, null); win.Repaint();

sb.Append("dataPath=").Append(UnityEngine.Application.dataPath)
  .Append(" form=").Append(layer.form == null ? ("<shape:" + layer.shapeForm + ">") : layer.form.GetType().Name)
  .Append(" pane=").Append(pane).Append(" win=").Append(win.position.width.ToString("F0"))
  .Append(" elements=").Append(ZAll(win.rootVisualElement).Count).Append("\n");
var lt = System.Type.GetType("UnityEditor.LogEntries,UnityEditor");
lt.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).Invoke(null, null);
return sb.ToString();
