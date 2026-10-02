var sb=new System.Text.StringBuilder();
foreach (var k in new string[]{"ZuiSectionToggleBar.ShaperWindow.userSel","ZuiSectionToggleBar.ShaperWindow.barMode","Shaper.lastView","ZuiSectionToggleBar.PyreWindow.userSel","ZuiSectionToggleBar.ChunksWindow.userSel"})
  sb.Append(k).Append(" hasStr=").Append(UnityEditor.EditorPrefs.HasKey(k)).Append(" v='").Append(UnityEditor.EditorPrefs.GetString(k,"<none>")).Append("'\n");
sb.Append("viewsAsset=").Append(UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Shaper/ShaperViews.asset")!=null).Append("\n");
sb.Append("shaperFolder: ");
foreach (var g in UnityEditor.AssetDatabase.FindAssets("", new string[]{"Assets/Shaper"})) sb.Append(UnityEditor.AssetDatabase.GUIDToAssetPath(g)).Append(" | ");
return sb.ToString();
