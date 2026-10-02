// Shaper setup. Reads EditorPrefs:
//   T0312.doc    asset path of the document to bind   (default the committed demo document)
//   T0312.layer  which layer to select                 (default 0)
//   T0312.pane   left-pane width -> ZuiColumnFlow(360) column count: <720 = 1, 720..1079 = 2, >=1440 = 4
//   T0312.winw   window width
// Opens the window, switches EVERY toggle-bar section on, binds, selects the layer, rebuilds, and reports.
// Never saves the document.
var sb = new System.Text.StringBuilder();
string docPath = UnityEditor.EditorPrefs.GetString("T0312.doc", "Assets/Demos/ShaperDemo/ShaperDemoDoc.asset");
int layerIx = UnityEditor.EditorPrefs.GetInt("T0312.layer", 0);
float pane = float.Parse(UnityEditor.EditorPrefs.GetString("T0312.pane", "400"));
float winw = float.Parse(UnityEditor.EditorPrefs.GetString("T0312.winw", "1700"));

var shT = ZType("ShaperWindow");
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;

UnityEditor.EditorPrefs.SetString("ZuiSectionToggleBar.ShaperWindow.userSel",
    "Views=1;Canvas=1;Layers=1;Shape=1;Fill=1;Swarm=1;SpriteFX=1;Lights=1;Tags=1");
UnityEditor.EditorPrefs.DeleteKey("ZuiSectionToggleBar.ShaperWindow.solo");
UnityEditor.EditorPrefs.SetFloat("ZUI.Split.shaper.window.split.v1", pane);

var win = ZWin("ShaperWindow");
if (win == null)
{
    UnityEditor.EditorApplication.ExecuteMenuItem("Laubrary/Shaper");
    win = ZWin("ShaperWindow");
}
if (win == null) return "NO SHAPER WINDOW";
win.position = new UnityEngine.Rect(20, 40, winw, 1000);
win.Show(); win.Repaint();

var doc = UnityEditor.AssetDatabase.LoadMainAssetAtPath(docPath);
if (doc == null) return "NO DOCUMENT at " + docPath;
bool dirtyBefore = UnityEditor.EditorUtility.IsDirty(doc);
System.Reflection.MethodInfo setAsset = null;
for (var t = shT; t != null && setAsset == null; t = t.BaseType)
    setAsset = t.GetMethod("SetAsset", BFi | System.Reflection.BindingFlags.DeclaredOnly);
setAsset.Invoke(win, new object[] { doc });

System.Reflection.FieldInfo selF = null;
for (var t = shT; t != null && selF == null; t = t.BaseType) selF = t.GetField("selectedLayer", BFi);
if (selF != null) selF.SetValue(win, layerIx);

System.Reflection.MethodInfo rebuild = null;
for (var t = shT; t != null && rebuild == null; t = t.BaseType)
    rebuild = t.GetMethod("Rebuild", BFi | System.Reflection.BindingFlags.DeclaredOnly);
rebuild.Invoke(win, null); win.Repaint();

var flowCols = 0;
foreach (var v in ZAll(win.rootVisualElement))
    if (v.GetType().Name == "ZuiColumnFlow") { flowCols = v.hierarchy.childCount > 0 ? v.hierarchy[0].hierarchy.childCount : 0; break; }

sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n")
  .Append("bound=").Append(doc.name).Append(" layer=").Append(layerIx)
  .Append(" dirtyBefore=").Append(dirtyBefore).Append(" dirtyAfter=").Append(UnityEditor.EditorUtility.IsDirty(doc))
  .Append(" pane=").Append(pane).Append(" win=").Append(win.position)
  .Append(" columns=").Append(flowCols)
  .Append(" elements=").Append(ZAll(win.rootVisualElement).Count).Append("\n");
var lt = System.Type.GetType("UnityEditor.LogEntries,UnityEditor");
lt.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).Invoke(null, null);
return sb.ToString();
