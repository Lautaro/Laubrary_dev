var sb = new System.Text.StringBuilder();
var scn = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
sb.Append("sceneDirtyBefore=").Append(scn.isDirty).Append("\n");
UnityEditor.EditorWindow win = null;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
  if (w != null && w.GetType().Name == "ZoePreviewWindow") win = w;
if (win == null) return "no window";
var t = win.GetType();
var f = t.GetField("_zoe", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
UnityEngine.Object proto = null;
foreach (var g in UnityEditor.AssetDatabase.FindAssets("ProtoGuy"))
{
  var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
  var o = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p);
  if (o != null && o.GetType().Name == "Zoe") { proto = o; sb.Append("zoe=").Append(p).Append("\n"); break; }
}
if (proto == null) return sb.Append("no ProtoGuy Zoe found\n").ToString();
f.SetValue(win, proto);
win.Focus(); win.Repaint();
sb.Append("bound, sceneDirtyAfter=").Append(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isDirty).Append("\n");
return sb.ToString();
