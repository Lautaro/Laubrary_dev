var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");
int nd = 0;
foreach (var o in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.ScriptableObject>())
{
    if (o == null || !UnityEditor.EditorUtility.IsDirty(o)) continue;
    var ap = UnityEditor.AssetDatabase.GetAssetPath(o);
    if (string.IsNullOrEmpty(ap)) continue;
    nd++; sb.Append("DIRTY ").Append(ap).Append("\n");
}
sb.Append("dirty assets = ").Append(nd).Append("\n");
foreach (var p in new string[] { "Assets/Pyre/New Pyre Plus.asset", "Assets/Pyre/New Pyre Plus 1.asset", "Assets/Pyre/Green Lantern.asset", "Assets/Shaper/New Shaper.asset", "Assets/Shaper/New Shaper 1.asset" })
{
    var o = UnityEditor.AssetDatabase.LoadMainAssetAtPath(p);
    sb.Append("  ").Append(p).Append(" dirty=").Append(o != null && UnityEditor.EditorUtility.IsDirty(o)).Append("\n");
}
return sb.ToString();
