var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var pyreT = FT("PyreWindow");
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
UnityEditor.EditorWindow win = null;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w0 != null && w0.GetType() == pyreT) win = w0;
sb.Append("win found=").Append(win != null).Append("\n");
sb.Append("MINSIZE = ").Append(win.minSize.x).Append(" x ").Append(win.minSize.y).Append("\n");

var assetT = FT("Pyre");
string p = "Assets/Pyre/New Pyre Plus.asset";
var asset = UnityEditor.AssetDatabase.LoadAssetAtPath(p, assetT);
sb.Append("asset loaded=").Append(asset != null).Append(" dirtyBefore=").Append(UnityEditor.EditorUtility.IsDirty(asset)).Append("\n");
// read the stored previewLayerSel before binding
var pls = assetT.GetField("previewLayerSel", BFi);
sb.Append("previewLayerSel on disk-loaded object BEFORE bind = ").Append(pls == null ? "<nofield>" : pls.GetValue(asset).ToString()).Append("\n");

System.Reflection.MethodInfo set = null;
for (var t = pyreT; t != null && set == null; t = t.BaseType) set = t.GetMethod("SetAsset", BFi | System.Reflection.BindingFlags.DeclaredOnly);
set.Invoke(win, new object[]{ asset });
sb.Append("AFTER BIND: dirty=").Append(UnityEditor.EditorUtility.IsDirty(asset))
  .Append(" previewLayerSel=").Append(pls.GetValue(asset)).Append("\n");

// SelLayer must still return a valid layer
System.Reflection.PropertyInfo selP = null;
for (var t = pyreT; t != null && selP == null; t = t.BaseType) selP = t.GetProperty("SelLayer", BFi);
object sel = null; try { sel = selP == null ? null : selP.GetValue(win); } catch (System.Exception e) { sel = "EX:" + e.Message; }
sb.Append("SelLayer = ").Append(sel == null ? "<null>" : sel.ToString()).Append(" dirtyAfterSelLayer=").Append(UnityEditor.EditorUtility.IsDirty(asset)).Append("\n");

// rebuild (the clamp path) must stay silent
System.Reflection.MethodInfo rebuild = null;
for (var t = pyreT; t != null && rebuild == null; t = t.BaseType) rebuild = t.GetMethod("Rebuild", BFi | System.Reflection.BindingFlags.DeclaredOnly);
rebuild.Invoke(win, null);
sb.Append("AFTER REBUILD: dirty=").Append(UnityEditor.EditorUtility.IsDirty(asset)).Append(" previewLayerSel=").Append(pls.GetValue(asset)).Append("\n");

int nd = 0;
foreach (var o in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.ScriptableObject>())
{
    if (o == null || !UnityEditor.EditorUtility.IsDirty(o)) continue;
    var ap = UnityEditor.AssetDatabase.GetAssetPath(o);
    if (string.IsNullOrEmpty(ap) || !ap.StartsWith("Assets/")) continue;
    nd++; sb.Append("DIRTY ").Append(ap).Append("\n");
}
sb.Append("dirtyCount=").Append(nd).Append("\n");
return sb.ToString();
