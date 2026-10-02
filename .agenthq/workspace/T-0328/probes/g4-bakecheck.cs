var sb = new System.Text.StringBuilder();
foreach (var g in UnityEditor.AssetDatabase.FindAssets("AuditT328ShaperA"))
{
    var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
    var o = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p);
    sb.Append("  ").Append(p).Append("  type=").Append(o == null ? "?" : o.GetType().Name).Append("\n");
    if (p.EndsWith(".png"))
    {
        var subs = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(p);
        int sprites = 0; foreach (var s in subs) if (s is UnityEngine.Sprite) sprites++;
        var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(p);
        sb.Append("     sheet ").Append(tex.width).Append("x").Append(tex.height).Append(" sprites=").Append(sprites).Append("\n");
    }
}
return sb.ToString();
