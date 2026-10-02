// T-0336 step 1 — cold-open Shaper at 1500x900 and report the empty state.
var sb = new System.Text.StringBuilder();
int closed = 0;
foreach (var w0 in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
    if (w0 != null && w0.GetType().Name == "ShaperWindow") { w0.Close(); closed++; }
sb.Append("closed=").Append(closed).Append("\n");
bool ok = UnityEditor.EditorApplication.ExecuteMenuItem("Laubrary/Shaper");
sb.Append("menu Laubrary/Shaper -> ").Append(ok).Append("\n");
var w = ZWin("ShaperWindow");
if (w == null) return sb.Append("NO WINDOW").ToString();
sb.Append("minSize=").Append(w.minSize).Append("\n");
w.position = new UnityEngine.Rect(30, 40, 1500, 900);
w.Repaint();
sb.Append("position=").Append(w.position).Append("\n");
sb.Append("userSel=").Append(UnityEditor.EditorPrefs.GetString("ZuiSectionToggleBar.ShaperWindow.userSel", "<unset>")).Append("\n");
sb.Append("T0312.out=").Append(UnityEditor.EditorPrefs.GetString("T0312.out","<unset>")).Append("\n");
sb.Append("T320.capOut=").Append(UnityEditor.EditorPrefs.GetString("T320.capOut","<unset>")).Append("\n");
sb.Append("T320.capWin=").Append(UnityEditor.EditorPrefs.GetString("T320.capWin","<unset>")).Append("\n");
sb.Append("Shaper.lastView=").Append(UnityEditor.EditorPrefs.GetString("Shaper.lastView","<unset>")).Append("\n");
var assets = UnityEditor.AssetDatabase.FindAssets("", new string[]{"Assets/Shaper"});
sb.Append("Assets/Shaper contents (").Append(assets.Length).Append("): ");
foreach (var g in assets) sb.Append(UnityEditor.AssetDatabase.GUIDToAssetPath(g)).Append(" | ");
return sb.ToString();
