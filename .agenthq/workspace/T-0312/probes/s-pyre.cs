// Pyre setup: create (once) a scratch spec Assets/Pyre/AuditT0312.asset with TWO layers — layer 0 a plain
// Disc (the default ShapeForm, no PyreForm) and layer 1 a Torch (TorchForm) — open Pyre, bind it, select
// the layer named by T0312.layer, size the window by T0312.winw and its dial pane by T0312.pane.
// The scratch asset is deleted by z-clean.cs; no pre-existing Pyre asset is touched.
var sb = new System.Text.StringBuilder();
int layerIx = UnityEditor.EditorPrefs.GetInt("T0312.layer", 0);
float pane = float.Parse(UnityEditor.EditorPrefs.GetString("T0312.pane", "400"));
float winw = float.Parse(UnityEditor.EditorPrefs.GetString("T0312.winw", "1700"));
const string path = "Assets/Pyre/AuditT0312.asset";

var spec = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Pyre.Pyre>(path);
if (spec == null)
{
    spec = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Pyre.Pyre>();
    spec.layers.Clear();
    var disc = new Laubrary.Pyre.PyreLayer { name = "Disc", matteEnabled = false };
    disc.shapeForm = Laubrary.Pyre.ShapeForm.Disc;
    spec.layers.Add(disc);
    var torch = new Laubrary.Pyre.PyreLayer { name = "Torch", matteEnabled = false };
    torch.form = new Laubrary.Pyre.Forms.Kiln.TorchForm();
    spec.layers.Add(torch);
    UnityEditor.AssetDatabase.CreateAsset(spec, path);
    UnityEditor.AssetDatabase.SaveAssetIfDirty(spec);
    sb.Append("created ").Append(path).Append("\n");
}
spec.previewLayerSel = layerIx;

UnityEditor.EditorPrefs.SetFloat("ZUI.Split.pyre.window.split.v1", pane);
var win = ZWin("PyreWindow");
if (win == null) { UnityEditor.EditorApplication.ExecuteMenuItem("Laubrary/Pyre"); win = ZWin("PyreWindow"); }
if (win == null) return "NO PYRE WINDOW";
win.position = new UnityEngine.Rect(20, 40, winw, 1000);
win.Show(); win.Repaint();

var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
var wt = win.GetType();
System.Reflection.MethodInfo setAsset = null;
for (var t = wt; t != null && setAsset == null; t = t.BaseType)
    setAsset = t.GetMethod("SetAsset", BFi | System.Reflection.BindingFlags.DeclaredOnly);
setAsset.Invoke(win, new object[] { spec });

System.Reflection.MethodInfo rebuild = null;
for (var t = wt; t != null && rebuild == null; t = t.BaseType)
    rebuild = t.GetMethod("Rebuild", BFi | System.Reflection.BindingFlags.DeclaredOnly);
rebuild.Invoke(win, null); win.Repaint();

sb.Append("dataPath=").Append(UnityEngine.Application.dataPath)
  .Append(" bound=").Append(spec.name).Append(" layers=").Append(spec.layers.Count)
  .Append(" sel=").Append(spec.previewLayerSel)
  .Append(" layer0.form=").Append(spec.layers[0].form == null ? "<none/Disc>" : spec.layers[0].form.GetType().Name)
  .Append(" layer1.form=").Append(spec.layers[1].form == null ? "<none/Disc>" : spec.layers[1].form.GetType().Name)
  .Append(" pane=").Append(pane).Append(" win=").Append(win.position)
  .Append(" elements=").Append(ZAll(win.rootVisualElement).Count).Append("\n");
var lt = System.Type.GetType("UnityEditor.LogEntries,UnityEditor");
lt.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).Invoke(null, null);
return sb.ToString();
