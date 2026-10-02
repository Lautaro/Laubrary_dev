var sb=new System.Text.StringBuilder();
sb.Append("delete store=").Append(UnityEditor.AssetDatabase.DeleteAsset("Assets/Shaper/ShaperViews.asset")).Append("\n");
UnityEditor.EditorPrefs.DeleteKey("Shaper.lastView");
UnityEditor.AssetDatabase.Refresh();
sb.Append("metaExists=").Append(System.IO.File.Exists(Application.dataPath+"/Shaper/ShaperViews.asset.meta")).Append("\n");
sb.Append("lastView='").Append(UnityEditor.EditorPrefs.GetString("Shaper.lastView","<none>")).Append("'\n");
return sb.ToString();
