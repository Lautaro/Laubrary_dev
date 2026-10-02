var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");

// the exact T-0293 repro: divider parked far right, then cold-open at the documented minimum
UnityEditor.EditorPrefs.SetFloat("ZUI.Split.shaper.window.split.v1", 1400f);
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<Laubrary.Shaper.Editor.ShaperWindow>()) w0.Close();
var doc = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>("Assets/Shaper/AuditA24a.asset");
Laubrary.Shaper.Editor.ShaperWindow.OpenFor(doc);
var win = UnityEditor.EditorWindow.GetWindow<Laubrary.Shaper.Editor.ShaperWindow>();
win.Show(); win.Focus();
sb.Append("minSize=").Append(win.minSize.x).Append("x").Append(win.minSize.y).Append("\n");
win.position = new UnityEngine.Rect(50, 50, 820, 520);
UnityEditor.SessionState.SetString("A24.t0296", "armed");
sb.Append("opened at 820x520 with pref=").Append(UnityEditor.EditorPrefs.GetFloat("ZUI.Split.shaper.window.split.v1", -1)).Append("\n");
return sb.ToString();
