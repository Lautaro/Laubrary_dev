// Hash four frames, then press the toolbar's own Save.
var win = ZWin("ShaperWindow"); if (win==null) return "no shaper";
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.FieldInfo stageF=null, frameF=null, assetF=null;
for (var t=win.GetType(); t!=null; t=t.BaseType) { if (stageF==null) stageF=t.GetField("stage",BFi); if (frameF==null) frameF=t.GetField("currentFrame",BFi); if (assetF==null) assetF=t.GetField("asset",BFi); }
var stage = stageF.GetValue(win);
var texF = stage.GetType().GetField("_tex", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
var refresh = stage.GetType().GetMethod("Refresh");
var sb = new System.Text.StringBuilder();
foreach (int f in new int[]{0,4,8,12}) {
  frameF.SetValue(win, f); refresh.Invoke(stage, null);
  var tex = texF.GetValue(stage) as UnityEngine.Texture2D; if (tex==null){sb.Append("f").Append(f).Append("=notex ");continue;}
  var px = tex.GetPixels32(); unchecked { uint h=2166136261u; int lit=0; foreach (var p in px){ if(p.a>8) lit++; h=(h^p.r)*16777619u;h=(h^p.g)*16777619u;h=(h^p.b)*16777619u;h=(h^p.a)*16777619u; }
    sb.Append("f").Append(f).Append("=").Append(h.ToString("X8")).Append("/").Append(lit).Append(" "); } }
UnityEditor.EditorPrefs.SetString("T334.preSave", sb.ToString());
var a = assetF.GetValue(win) as UnityEngine.Object;
UnityEditor.EditorPrefs.SetString("T334.savePath", UnityEditor.AssetDatabase.GetAssetPath(a));
UnityEngine.UIElements.Button save = null;
foreach (var e in ZAll(win.rootVisualElement)) { var b=e as UnityEngine.UIElements.Button; if (b!=null && ZDrawn(b) && b.text=="Save") save=b; }
if (save == null) return "PRE " + sb + " | no Save button";
bool dis = !save.enabledInHierarchy;
ZClick(save);
return "PRE " + sb + "| path=" + UnityEditor.AssetDatabase.GetAssetPath(a) + " saveWasDisabled=" + dis + " dirtyAfter=" + UnityEditor.EditorUtility.IsDirty(a);
