// T-0313 — how wide is the compact-row defect?  Replays ZuiReflect.IsCompactRowElement's rule (private)
// over every [Serializable] element type of every List<T> field in every Laubrary runtime type, and prints
// the ones it admits with their field count — 2 dials fit a 360 px column, 3+ cannot.
var sb = new System.Text.StringBuilder();
var seen = new System.Collections.Generic.HashSet<string>();
System.Func<System.Type, int> Score = t =>
{
    if (!t.IsClass || t == typeof(string)) return -1;
    if (typeof(UnityEngine.Object).IsAssignableFrom(t)) return -1;
    int n = 0;
    foreach (var f in t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
    {
        if (f.IsNotSerialized) continue;
        foreach (var a in f.GetCustomAttributes(true))
        {
            var an = a.GetType().Name;
            if (an == "ZUIShowIfAttribute" || an == "ZUIPair2DAttribute") return -1;
        }
        var ft = f.FieldType;
        if (ft == typeof(bool)) { n++; continue; }
        bool ranged = false;
        foreach (var a in f.GetCustomAttributes(true)) if (a is UnityEngine.RangeAttribute) ranged = true;
        if ((ft == typeof(float) || ft == typeof(int)) && ranged) { n++; continue; }
        return -1;
    }
    return (n >= 2 && n <= 5) ? n : -1;
};

foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
{
    if (!a.GetName().Name.Contains("Laubrary")) continue;
    System.Type[] ts; try { ts = a.GetTypes(); } catch { continue; }
    foreach (var t in ts)
        foreach (var f in t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
        {
            if (!f.FieldType.IsGenericType) continue;
            if (f.FieldType.GetGenericTypeDefinition() != typeof(System.Collections.Generic.List<>)) continue;
            var et = f.FieldType.GetGenericArguments()[0];
            int n = Score(et);
            if (n < 0) continue;
            string key = t.Name + "." + f.Name + " -> " + et.Name;
            if (!seen.Add(key)) continue;
            sb.Append(n >= 3 ? "OVERFLOWS " : "fits      ").Append("dials=").Append(n).Append("  ").Append(key).Append("\n");
        }
}
sb.Append("(a compact row is 26px index + N x 100px dials + 6.2px gaps + a 22px x; a 360px dial pane gives ")
  .Append("a card body 293.8px, so N=2 is 258.6 and fits, N=3 is 364.8 and does not)\n");
return ZDump("compact-scan.txt", sb.ToString()) + "\n" + sb.ToString();
