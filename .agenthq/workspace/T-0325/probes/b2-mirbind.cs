// Create a scratch Mirage view and bind the window to it, so the stage row is on screen.
var t = ZType("MirageView");
if (t == null) return "no MirageView type";
string path = "Assets/Mirage/AuditT325View.asset";
if (UnityEditor.AssetDatabase.LoadAssetAtPath(path, t) == null)
{
    var so = UnityEngine.ScriptableObject.CreateInstance(t);
    UnityEditor.AssetDatabase.CreateAsset(so, path);
    UnityEditor.AssetDatabase.SaveAssets();
}
var r = ZBind("MirageWindow", path);
var w = ZWin("MirageWindow");
var sb = new System.Text.StringBuilder();
sb.Append(r).Append("\n");
if (w != null)
{
    foreach (var e in ZAll(w.rootVisualElement))
    {
        var b = e as UnityEngine.UIElements.Button;
        if (b == null || b.text != "Open preview stage") continue;
        sb.Append("BUTTON '").Append(b.text).Append("' enabled=").Append(b.enabledInHierarchy)
          .Append(" rect=").Append(b.worldBound).Append("\n");
        sb.Append("TIP=").Append(ZTip(b)).Append("\n");
    }
    sb.Append("elements=").Append(ZAll(w.rootVisualElement).Count).Append("\n");
}
return sb.ToString();
