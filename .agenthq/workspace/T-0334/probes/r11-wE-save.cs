// ZClick(e): press an element the way a mouse does — PointerDown then PointerUp at its own centre, with a
// real pointer id, which is what a Clickable manipulator listens for.  Returns false if it could not.
// Hash the document at four frames, then press the toolbar's own Save.
var win = ZWin("ShaperWindow"); if (win==null) return "no shaper";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo stageF=null, frameF=null, assetF=null;
for (var t = win.GetType(); t != null; t = t.BaseType) { if (stageF==null) stageF=t.GetField("stage",BFi); if (frameF==null) frameF=t.GetField("currentFrame",BFi); if (assetF==null) assetF=t.GetField("asset",BFi); }
var stage = stageF.GetValue(win);
var texF = stage.GetType().GetField("_tex", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
var refresh = stage.GetType().GetMethod("Refresh");
var sb = new System.Text.StringBuilder();
foreach (int f in new int[]{0,4,8,12}) {
  frameF.SetValue(win, f); refresh.Invoke(stage, null);
  var tex = texF.GetValue(stage) as UnityEngine.Texture2D; if (tex == null) { sb.Append("f").Append(f).Append("=notex "); continue; }
  var px = tex.GetPixels32(); unchecked { uint h = 2166136261u; int lit=0; foreach (var p in px) { if (p.a>8) lit++; h=(h^p.r)*16777619u; h=(h^p.g)*16777619u; h=(h^p.b)*16777619u; h=(h^p.a)*16777619u; }
    sb.Append("f").Append(f).Append("=").Append(h.ToString("X8")).Append("/").Append(lit).Append(" "); }
}
UnityEditor.EditorPrefs.SetString("T334.preSave", sb.ToString());
// press Save
UnityEngine.UIElements.VisualElement toolbar = null;
foreach (var e in ZAll(win.rootVisualElement)) if (e is UnityEditor.UIElements.ObjectField && ZDrawn(e)) { toolbar = e.hierarchy.parent; break; }
UnityEngine.UIElements.Button save = null;
for (int i=0;i<toolbar.hierarchy.childCount;i++) { var b = toolbar.hierarchy[i] as UnityEngine.UIElements.Button; if (b != null && b.text == "Save") save = b; }
if (save == null) return "PRE " + sb + " | no Save button";
bool disabled = !save.enabledInHierarchy;
ZClick(save);
var a = assetF.GetValue(win) as UnityEngine.Object;
return "PRE " + sb + "| saveWasDisabled=" + disabled + " dirty=" + UnityEditor.EditorUtility.IsDirty(a);
