const string path = "Assets/Pyre/AuditT0320.asset";
var spec = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Pyre.Pyre>(path);
if (spec == null) {
  spec = UnityEngine.ScriptableObject.CreateInstance<Laubrary.Pyre.Pyre>();
  spec.layers.Clear();
  var jet = new Laubrary.Pyre.PyreLayer { name = "Jet", matteEnabled = false };
  jet.form = new Laubrary.Pyre.Forms.Kiln.ExplosiveJetForm();
  spec.layers.Add(jet);
  UnityEditor.AssetDatabase.CreateAsset(spec, path);
  UnityEditor.AssetDatabase.SaveAssetIfDirty(spec);
}
spec.previewLayerSel = 0;
var win = ZWin("PyreWindow"); if (win==null) return "no pyre window";
win.position = new UnityEngine.Rect(0, 20, 1000, 900);
win.titleContent = new GUIContent("T320Pyre");
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.MethodInfo setAsset = null, rebuild = null;
for (var t = win.GetType(); t != null; t = t.BaseType) { if (setAsset==null) setAsset = t.GetMethod("SetAsset", BFi|System.Reflection.BindingFlags.DeclaredOnly); if (rebuild==null) rebuild = t.GetMethod("Rebuild", BFi|System.Reflection.BindingFlags.DeclaredOnly); }
setAsset.Invoke(win, new object[]{ spec });
rebuild.Invoke(win, null); win.Repaint();
var secT = ZType("ZuiSection"); var isOpen = secT.GetProperty("IsOpen"); int n=0;
foreach (var e in ZAll(win.rootVisualElement)) if (secT.IsInstanceOfType(e)) { isOpen.SetValue(e, true); n++; }
return "jet spec bound, sections=" + n + " win=" + win.position;
