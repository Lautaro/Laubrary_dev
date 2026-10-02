var sb = new System.Text.StringBuilder();
foreach (var wn in new string[]{"ShaperWindow","ZoeWindow","LarderWindow","MirageWindow","PyreWindow","LatheWindow","SpriteFxStackWindow","TextSplashWindow"})
{
    string k = "ZuiSectionToggleBar." + wn + ".userSel";
    string k2 = "ZuiSectionToggleBar." + wn + ".barMode";
    sb.AppendLine(k + " = '" + UnityEditor.EditorPrefs.GetString(k, "<unset>") + "'  barMode=" + (UnityEditor.EditorPrefs.HasKey(k2) ? UnityEditor.EditorPrefs.GetBool(k2).ToString() : "<unset>"));
}
return sb.ToString();
