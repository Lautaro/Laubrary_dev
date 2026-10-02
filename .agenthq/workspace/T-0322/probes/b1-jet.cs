const string path = "Assets/Shaper/AuditT322Pyre.asset";
var spec = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Pyre.Pyre>(path);
spec.layers.Clear();
var jet = new Laubrary.Pyre.PyreLayer { name = "Jet", matteEnabled = false };
jet.form = new Laubrary.Pyre.Forms.Kiln.ExplosiveJetForm();
spec.layers.Add(jet);
spec.previewLayerSel = 0;
spec.previewZoom = 3f;
UnityEditor.EditorUtility.SetDirty(spec); UnityEditor.AssetDatabase.SaveAssetIfDirty(spec);
var win = ZWin("PyreWindow");
win.position = new UnityEngine.Rect(20, 20, 820, 900);
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.MethodInfo setAsset=null, rebuild=null;
for (var t=win.GetType(); t!=null; t=t.BaseType) { if (setAsset==null) setAsset=t.GetMethod("SetAsset", BFi|System.Reflection.BindingFlags.DeclaredOnly); if (rebuild==null) rebuild=t.GetMethod("Rebuild", BFi|System.Reflection.BindingFlags.DeclaredOnly); }
setAsset.Invoke(win, new object[]{ spec }); rebuild.Invoke(win, null); win.Repaint();
var secT = ZType("ZuiSection"); var isOpen = secT.GetProperty("IsOpen"); int n=0;
foreach (var e in ZAll(win.rootVisualElement)) if (secT.IsInstanceOfType(e)) { isOpen.SetValue(e, true); n++; }
return "bound jet, sections=" + n + " win=" + win.position;
