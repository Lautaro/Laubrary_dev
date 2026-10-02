// Records Shaper's split pref, binds a document, reports the tree makeup.
System.Func<string, System.Type> FT = n => { foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) foreach (var t in a.GetTypes()) if (t.Name == n) return t; return null; };
var BFi = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;
var shT = FT("ShaperWindow");
UnityEditor.EditorWindow win = null;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>()) if (w0 != null && w0.GetType() == shT) win = w0;
var sb = new System.Text.StringBuilder();
sb.Append("splitPref=").Append(UnityEditor.EditorPrefs.GetFloat("ZUI.Split.shaper.window.split.v1", -1f)).Append("\n");
sb.Append("pos=").Append(win.position.ToString()).Append("\n");
var doc = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.ScriptableObject>("Assets/Shaper/New Shaper.asset");
sb.Append("doc=").Append(doc == null ? "NULL" : doc.name).Append("\n");
System.Reflection.MethodInfo setAsset = null;
for (var t = shT; t != null && setAsset == null; t = t.BaseType) setAsset = t.GetMethod("SetAsset", BFi | System.Reflection.BindingFlags.DeclaredOnly);
setAsset.Invoke(win, new object[] { doc });
sb.Append("dirty=").Append(UnityEditor.EditorUtility.IsDirty(doc)).Append("\n");
System.Action<UnityEngine.UIElements.VisualElement, System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>> Walk = null;
Walk = (e, into) => { if (e == null) return; into.Add(e); for (int i = 0; i < e.hierarchy.childCount; i++) Walk(e.hierarchy[i], into); };
var all = new System.Collections.Generic.List<UnityEngine.UIElements.VisualElement>(); Walk(win.rootVisualElement, all);
int imgs = 0, fields = 0, grads = 0;
foreach (var v in all)
{
    if (v is UnityEngine.UIElements.Image) imgs++;
    if (v.ClassListContains("zui-field")) fields++;
    if (v.GetType().Name == "ZuiGradientControl" || v.GetType().Name == "ZuiFillControl") grads++;
}
sb.Append("elements=").Append(all.Count).Append(" images=").Append(imgs).Append(" fields=").Append(fields).Append(" gradientControls=").Append(grads).Append("\n");
return sb.ToString();
