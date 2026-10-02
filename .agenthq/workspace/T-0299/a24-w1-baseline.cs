var sb = new System.Text.StringBuilder();
sb.Append("dataPath=").Append(UnityEngine.Application.dataPath).Append("\n");

System.Func<string, System.Type> FindType = n => {
    foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
        foreach (var t in a.GetTypes()) if (t.Name == n) return t;
    return null;
};

int closed = 0;
foreach (var w in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
{
    if (w == null) continue;
    var tn = w.GetType().Name;
    if (tn == "PyreWindow" || tn == "ShaperWindow") { sb.Append("closing ").Append(tn).Append("\n"); w.Close(); closed++; }
}
sb.Append("closed=").Append(closed).Append("\n");

string[] paths = {
  "Assets/Pyre/New Pyre Plus.asset",
  "Assets/Pyre/New Pyre Plus 1.asset",
  "Assets/Shaper/New Shaper.asset",
  "Assets/Shaper/New Shaper 1.asset"
};
foreach (var p in paths)
{
    var o = UnityEditor.AssetDatabase.LoadMainAssetAtPath(p);
    sb.Append("BASE ").Append(p).Append(" loaded=").Append(o != null)
      .Append(" type=").Append(o == null ? "?" : o.GetType().Name)
      .Append(" dirty=").Append(o == null ? false : UnityEditor.EditorUtility.IsDirty(o))
      .Append("\n");
}
return sb.ToString();
