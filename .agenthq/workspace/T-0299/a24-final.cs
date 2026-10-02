var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
UnityEditor.EditorPrefs.SetString("ZuiSectionToggleBar.ShaperWindow.userSel",
    "Views=1;Canvas=0;Layers=0;Shape=0;Fill=0;Swarm=0;SpriteFX=0;Lights=1;Tags=0");
sb.Append("userSel restored = '").Append(UnityEditor.EditorPrefs.GetString("ZuiSectionToggleBar.ShaperWindow.userSel","")).Append("'\n");

// FINAL COLD OPEN, from the menu, with everything closed first
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.Shaper.Editor.ShaperWindow>()) w0.Close();
UnityEditor.EditorApplication.ExecuteMenuItem("Laubrary/Shaper");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
win.position = new UnityEngine.Rect(60, 60, 1600, 1100); win.Show();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo curP = null;
for (var t = win.GetType(); t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
var d = curP.GetValue(win) as UnityEngine.Object;
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.childCount; i++) Walk(e[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
int b = 0, notip = 0;
foreach (var v in all) if (v is UnityEngine.UIElements.Button bb) { b++; if (string.IsNullOrEmpty(bb.tooltip)) notip++; }
sb.Append("COLD OPEN: bound=").Append(d == null ? "<empty state>" : d.name).Append(" title=").Append(win.titleContent.text)
  .Append(" elements=").Append(all.Count).Append(" buttons=").Append(b).Append(" withoutTooltip=").Append(notip).Append("\n");

// nothing dirty anywhere
int nd = 0;
foreach (var o in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.ScriptableObject>())
{ if (o == null || !UnityEditor.EditorUtility.IsDirty(o)) continue;
  var ap = UnityEditor.AssetDatabase.GetAssetPath(o); if (string.IsNullOrEmpty(ap)) continue;
  nd++; sb.Append("  STILL DIRTY ").Append(ap).Append("\n"); }
sb.Append("dirty assets = ").Append(nd).Append("\n");
sb.Append("compile failed = ").Append(UnityEditor.EditorUtility.scriptCompilationFailed).Append("\n");
return sb.ToString();
