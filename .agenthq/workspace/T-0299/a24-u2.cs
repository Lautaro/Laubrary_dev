var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
var WT = win.GetType();
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.PropertyInfo curP = null;
for (var t = WT; t != null; t = t.BaseType) { curP = t.GetProperty("Current", BFi); if (curP != null) break; }
var doc = curP.GetValue(win) as Laubrary.Shaper.ShaperDocument;
var changeM = WT.GetMethod("Change", BFi);
System.Reflection.MethodInfo rb = null;
for (var t = WT; t != null; t = t.BaseType) { rb = t.GetMethod("Rebuild", BFi); if (rb != null) break; }
sb.Append("doc=").Append(doc == null ? "<none>" : doc.name).Append("\n");
// ONE edit this frame: rename the layer (a plain field edit)
changeM.Invoke(win, new object[] { (System.Action)(() => doc.layers[0].name = "EditOne") });
rb.Invoke(win, null);
sb.Append("layerName now=").Append(doc.layers[0].name).Append(" dirty=").Append(UnityEditor.EditorUtility.IsDirty(doc)).Append("\n");
return sb.ToString();
