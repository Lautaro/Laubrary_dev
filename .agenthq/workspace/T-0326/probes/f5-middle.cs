// Tail elision vs MIDDLE elision, measured over every asset name any LauAsset grid can show in this
// project (every ScriptableObject under Assets, which is the superset of every browser's item list).
var style = UnityEditor.EditorStyles.miniLabel;
float cell = 104f;
System.Func<string,string> tail = s => {
    if (string.IsNullOrEmpty(s)) return s;
    var p = new GUIContent(s); if (style.CalcSize(p).x <= cell) return s;
    for (int k = s.Length - 1; k > 0; k--) { p.text = s.Substring(0, k) + "…"; if (style.CalcSize(p).x <= cell) return p.text; }
    return "…";
};
System.Func<string,string> mid = s => {
    if (string.IsNullOrEmpty(s)) return s;
    var p = new GUIContent(s); if (style.CalcSize(p).x <= cell) return s;
    for (int drop = 1; drop < s.Length; drop++)
    {
        int keep = s.Length - drop; if (keep < 2) break;
        int head = (keep + 1) / 2, tailn = keep - head;
        p.text = s.Substring(0, head) + "…" + s.Substring(s.Length - tailn);
        if (style.CalcSize(p).x <= cell) return p.text;
    }
    return "…";
};
var names = new System.Collections.Generic.List<string>();
foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:ScriptableObject", new string[]{ "Assets" }))
{
    var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
    if (p.Contains("/Samples~/")) continue;
    var o = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p);
    if (o != null) names.Add(o.name);
}
System.Func<System.Func<string,string>,string,string> report = (f, tag) => {
    var seen = new System.Collections.Generic.Dictionary<string,int>();
    int over = 0, coll = 0;
    foreach (var n in names)
    {
        var s = f(n); if (s != n) over++;
        int c; seen.TryGetValue(s, out c); seen[s] = c + 1;
    }
    var dup = new System.Text.StringBuilder();
    foreach (var kv in seen) if (kv.Value > 1) { coll += kv.Value - 1; if (dup.Length < 1200) dup.Append("   '").Append(kv.Key).Append("' x").Append(kv.Value).Append("\n"); }
    return tag + ": names=" + names.Count + " elided=" + over + " collidingExtras=" + coll + "\n" + dup;
};
// how many of the collisions are genuinely identical FULL names (authored duplicates, not a UI fault)
var full = new System.Collections.Generic.Dictionary<string,int>(); int fullDup = 0;
foreach (var n in names) { int c; full.TryGetValue(n, out c); full[n] = c + 1; }
foreach (var kv in full) if (kv.Value > 1) fullDup += kv.Value - 1;
return "identical FULL names in the project: " + fullDup + "\n" + report(tail, "TAIL  ") + report(mid, "MIDDLE");
