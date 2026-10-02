var sb = new System.Text.StringBuilder();
foreach (var g in UnityEditor.AssetDatabase.FindAssets("", new string[]{"Assets/Shaper"}))
  sb.Append(UnityEditor.AssetDatabase.GUIDToAssetPath(g)).Append("\n");
sb.Append("-- prefs --\n");
foreach (var k in new string[]{"ZuiSectionToggleBar.ShaperWindow.userSel","T0312.out","T320.capOut","T320.capWin","Shaper.lastView"})
  sb.Append(k).Append(" = ").Append(UnityEditor.EditorPrefs.GetString(k,"<unset>")).Append("\n");
return sb.ToString();
