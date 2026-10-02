// Force the asset off disk and back, rebind it, and hash the same four frames.
var win = ZWin("ShaperWindow"); if (win==null) return "no shaper";
const string path = "Assets/Shaper/AuditT0320W2.asset";
UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceUpdate | UnityEditor.ImportAssetOptions.ForceSynchronousImport);
var doc = UnityEditor.AssetDatabase.LoadMainAssetAtPath(path);
var BFi = System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.FlattenHierarchy;
System.Reflection.MethodInfo setAsset=null; System.Reflection.FieldInfo stageF=null, frameF=null;
for (var t = win.GetType(); t != null; t = t.BaseType) { if (setAsset==null) setAsset=t.GetMethod("SetAsset", BFi|System.Reflection.BindingFlags.DeclaredOnly); if (stageF==null) stageF=t.GetField("stage",BFi); if (frameF==null) frameF=t.GetField("currentFrame",BFi); }
setAsset.Invoke(win, new object[]{ doc });
var stage = stageF.GetValue(win);
var invalidate = stage.GetType().GetMethod("InvalidateFrameCache"); if (invalidate != null) invalidate.Invoke(stage, null);
var texF = stage.GetType().GetField("_tex", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
var refresh = stage.GetType().GetMethod("Refresh");
var sb = new System.Text.StringBuilder();
foreach (int f in new int[]{0,4,8,12}) {
  frameF.SetValue(win, f); refresh.Invoke(stage, null);
  var tex = texF.GetValue(stage) as UnityEngine.Texture2D; if (tex == null) { sb.Append("f").Append(f).Append("=notex "); continue; }
  var px = tex.GetPixels32(); unchecked { uint h = 2166136261u; int lit=0; foreach (var p in px) { if (p.a>8) lit++; h=(h^p.r)*16777619u; h=(h^p.g)*16777619u; h=(h^p.b)*16777619u; h=(h^p.a)*16777619u; }
    sb.Append("f").Append(f).Append("=").Append(h.ToString("X8")).Append("/").Append(lit).Append(" "); }
}
string pre = UnityEditor.EditorPrefs.GetString("T320.preSave","");
return "PRE  " + pre + "\nPOST " + sb + "\nIDENTICAL=" + (pre.Trim() == sb.ToString().Trim());
