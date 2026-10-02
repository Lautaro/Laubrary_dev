var sb = new System.Text.StringBuilder();
UnityEditor.EditorApplication.ExecuteMenuItem("Laubrary/Zoetrope/Zoe Preview");
UnityEditor.EditorWindow win = null;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
  if (w != null && w.GetType().Name == "ZoePreviewWindow") win = w;
if (win == null) return "no window";
sb.Append(ZSummary("zoeprev-empty (before binding)"));
sb.Append(ZAudit(win, "zoeprev-empty").Length > 0 ? "" : "");
sb.Append(ZSummary("zoeprev-empty"));
var proto = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Demos/ProtoGuyDemo/ProtoGuy.asset");
var f = win.GetType().GetField("_zoe", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
f.SetValue(win, proto);
var rebuild = win.GetType().GetMethod("Rebuild", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
rebuild.Invoke(win, null);
win.Focus(); win.Repaint();
sb.Append("bound ProtoGuy, sceneDirty=").Append(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isDirty).Append("\n");
sb.Append("pos=").Append(win.position).Append("\n");
return sb.ToString();
