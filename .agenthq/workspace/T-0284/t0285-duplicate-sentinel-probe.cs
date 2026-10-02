// A19 — T-0285 verification, part A: build a rich source document + an unrelated dirty sentinel,
// bind the source in the real window, press its real Duplicate button, and read the sentinel back.
var SB = new System.Text.StringBuilder();
var BFs = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
var WT = typeof(Laubrary.Shaper.Editor.ShaperWindow);
var newLayerM = WT.GetMethod("NewLayer", BFs);

string DIR = "Assets/Shaper/AuditA19";
if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Shaper")) UnityEditor.AssetDatabase.CreateFolder("Assets", "Shaper");
if (!UnityEditor.AssetDatabase.IsValidFolder(DIR)) UnityEditor.AssetDatabase.CreateFolder("Assets/Shaper", "AuditA19");

// ── source document: three layers, one of them a hosted Pyre form (a real SerializeReference graph) ──
var src = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
src.canvasWidth = 64; src.canvasHeight = 64; src.frameCount = 8; src.seed = 4242u;
src.lightRig.lights.Add(new Laubrary.Shaper.ShaperLight { name = "Key", enabled = true });
var la = (Laubrary.Shaper.ShaperLayer)newLayerM.Invoke(null, new object[] { "Star", src });
la.root.primitive.kind = Laubrary.Shaper.ShaperPrimitiveKind.Star;
src.layers.Add(la);
var lb = (Laubrary.Shaper.ShaperLayer)newLayerM.Invoke(null, new object[] { "Solid", src });
lb.root.kind = Laubrary.Shaper.ShaperNodeKind.Solid;
src.layers.Add(lb);
var lc = (Laubrary.Shaper.ShaperLayer)newLayerM.Invoke(null, new object[] { "Arc", src });
lc.root.kind = Laubrary.Shaper.ShaperNodeKind.Composite;
var pf = new Laubrary.PyreShaper.PyreFormCompositeSource();
pf.form = new Laubrary.Pyre.Forms.Kiln.ArcBurstForm();
lc.root.composite.source = pf;
src.layers.Add(lc);
string srcPath = DIR + "/A19Src.asset";
UnityEditor.AssetDatabase.DeleteAsset(srcPath);
UnityEditor.AssetDatabase.CreateAsset(src, srcPath);
UnityEditor.AssetDatabase.SaveAssetIfDirty(src);

// ── the sentinel: a saved, unrelated asset that is then edited in memory and left DIRTY ─────────────
string senPath = DIR + "/A19Sentinel.asset";
UnityEditor.AssetDatabase.DeleteAsset(senPath);
var sen = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Shaper.ShaperDocument>();
sen.canvasWidth = 32; sen.canvasHeight = 32; sen.frameCount = 2;
UnityEditor.AssetDatabase.CreateAsset(sen, senPath);
UnityEditor.AssetDatabase.SaveAssetIfDirty(sen);
UnityEditor.AssetDatabase.Refresh();
string senAbs = System.IO.Path.GetFullPath(senPath);
sen.canvasWidth = 199;                      // the unsaved edit
UnityEditor.EditorUtility.SetDirty(sen);
string beforeText = System.IO.File.ReadAllText(senAbs);
var beforeTime = System.IO.File.GetLastWriteTimeUtc(senAbs);
SB.Append("sentinel before: dirty=").Append(UnityEditor.EditorUtility.IsDirty(sen))
  .Append(" fileHasWidth199=").Append(beforeText.Contains("canvasWidth: 199"))
  .Append(" bytes=").Append(beforeText.Length).Append('\n');

// ── bind the source in the real window and press its own Duplicate button ───────────────────────────
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.Shaper.Editor.ShaperWindow>()) w.Close();
UnityEditor.EditorApplication.ExecuteMenuItem("Laubrary/Shaper");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
win.position = new UnityEngine.Rect(80, 60, 1500, 1150);
win.Show(); win.Repaint();
System.Reflection.MethodInfo setAssetM = null;
for (var t = WT; t != null; t = t.BaseType) { setAssetM = t.GetMethod("SetAsset", BFi); if (setAssetM != null) break; }
setAssetM.Invoke(win, new object[] { src });
win.Repaint();

System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> WalkT = null;
WalkT = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) WalkT(e[i], into); };
System.Func<System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Tree = () =>
{ var l = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); WalkT(win.rootVisualElement, l); return l; };

UnityEngine.UIElements.Button dupBtn = null;
foreach (var v in Tree()) if (v is UnityEngine.UIElements.Button b && b.text == "Duplicate") dupBtn = b;
SB.Append("Duplicate button found=").Append(dupBtn != null).Append(" enabled=").Append(dupBtn != null && dupBtn.enabledInHierarchy).Append('\n');
if (dupBtn != null)
{
    using (var ev = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) { ev.target = dupBtn; dupBtn.SendEvent(ev); }
}

System.Reflection.PropertyInfo curP = null;
for (var t = WT; t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
var dup = curP.GetValue(win) as Laubrary.Shaper.ShaperDocument;
SB.Append("after Duplicate: window now bound to=").Append(dup == null ? "<none>" : dup.name)
  .Append(" path=").Append(dup == null ? "" : UnityEditor.AssetDatabase.GetAssetPath(dup)).Append('\n');

string afterText = System.IO.File.ReadAllText(senAbs);
var afterTime = System.IO.File.GetLastWriteTimeUtc(senAbs);
SB.Append("sentinel after: dirty=").Append(UnityEditor.EditorUtility.IsDirty(sen))
  .Append(" fileHasWidth199=").Append(afterText.Contains("canvasWidth: 199"))
  .Append(" fileUnchanged=").Append(afterText == beforeText)
  .Append(" mtimeUnchanged=").Append(afterTime == beforeTime)
  .Append(" inMemoryWidth=").Append(sen.canvasWidth).Append('\n');
UnityEditor.EditorPrefs.SetString("A19.dupPath", dup == null ? "" : UnityEditor.AssetDatabase.GetAssetPath(dup));
UnityEditor.EditorPrefs.SetString("A19.srcPath", srcPath);
return SB.ToString();
