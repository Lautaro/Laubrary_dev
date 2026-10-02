// A19 — T-0285 verification, part B: the duplicate renders identically, and shares no managed object.
var SB = new System.Text.StringBuilder();
string srcPath = UnityEditor.EditorPrefs.GetString("A19.srcPath");
string dupPath = UnityEditor.EditorPrefs.GetString("A19.dupPath");
var src = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>(srcPath);
var dup = UnityEditor.AssetDatabase.LoadAssetAtPath<Laubrary.Shaper.ShaperDocument>(dupPath);
SB.Append("src=").Append(srcPath).Append(" dup=").Append(dupPath)
  .Append(" bothLoaded=").Append(src != null && dup != null).Append('\n');

foreach (int f in new[] { 0, 4, 7 })
{
    var a = Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(src, f);
    var b = Laubrary.Shaper.ShaperDocumentRenderer.RenderFrame(dup, f);
    int diff = 0, lit = 0;
    for (int i = 0; i < a.Length; i++) { if (a[i].a > 0) lit++; if (!a[i].Equals(b[i])) diff++; }
    SB.Append("frame ").Append(f).Append(": srcLit=").Append(lit).Append(" diffPx=").Append(diff).Append('\n');
}

var BF = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
// every managed class instance reachable from a document (Unity objects excluded, they are shared by design)
System.Func<Laubrary.Shaper.ShaperDocument, System.Collections.Generic.List<object>> Collect = root =>
{
    var found = new System.Collections.Generic.List<object>();
    var seen = new System.Collections.Generic.HashSet<object>();
    var stack = new System.Collections.Generic.Stack<object>();
    stack.Push(root);
    int guard = 0;
    while (stack.Count > 0 && guard++ < 300000)
    {
        var o = stack.Pop();
        if (o == null) continue;
        var t = o.GetType();
        if (t.IsPrimitive || t.IsEnum || o is string) continue;
        bool isVal = t.IsValueType;
        if (!isVal) { if (!seen.Add(o)) continue; }
        if (!isVal && !ReferenceEquals(o, root) && !(o is UnityEngine.Object)) found.Add(o);
        if (o is UnityEngine.Object && !ReferenceEquals(o, root)) continue;
        if (o is System.Collections.IEnumerable en2 && !(o is string))
        { foreach (var it in en2) if (it != null) stack.Push(it); continue; }
        foreach (var fi in t.GetFields(BF))
        {
            if (fi.FieldType.IsPrimitive || fi.FieldType.IsEnum || fi.FieldType == typeof(string)) continue;
            object v = null; try { v = fi.GetValue(o); } catch { }
            if (v != null) stack.Push(v);
        }
    }
    return found;
};
var A = Collect(src);
var B = Collect(dup);
var setA = new System.Collections.Generic.HashSet<object>(A);
int shared = 0; string firstShared = "";
foreach (var o in B) if (setA.Contains(o)) { shared++; if (firstShared == "") firstShared = o.GetType().FullName; }
SB.Append("graph objects: src=").Append(A.Count).Append(" dup=").Append(B.Count)
  .Append(" SHARED=").Append(shared).Append(firstShared == "" ? "" : " first=" + firstShared).Append('\n');

int nullMismatch = 0; string firstNull = "";
var visited = new System.Collections.Generic.HashSet<object>();
System.Action<object, object, string, int> Cmp = null;
Cmp = (x, y, path, depth) =>
{
    if (depth > 12 || x == null) return;
    var t = x.GetType();
    if (t.IsPrimitive || t.IsEnum || x is string) return;
    if (!t.IsValueType && !visited.Add(x)) return;
    if (y == null) { nullMismatch++; if (firstNull == "") firstNull = path; return; }
    if (x is System.Collections.IList lx && y is System.Collections.IList ly)
    {
        if (lx.Count != ly.Count) { nullMismatch++; if (firstNull == "") firstNull = path + ".Count " + lx.Count + "!=" + ly.Count; return; }
        for (int i = 0; i < lx.Count; i++) { if (lx[i] != null && ly[i] == null) { nullMismatch++; if (firstNull == "") firstNull = path + "[" + i + "]"; } else if (lx[i] != null) Cmp(lx[i], ly[i], path + "[" + i + "]", depth + 1); }
        return;
    }
    if (x is UnityEngine.Object) return;
    foreach (var fi in t.GetFields(BF))
    {
        if (fi.FieldType.IsPrimitive || fi.FieldType.IsEnum || fi.FieldType == typeof(string)) continue;
        object xv = null, yv = null;
        try { xv = fi.GetValue(x); yv = fi.GetValue(y); } catch { continue; }
        if (xv != null && yv == null) { nullMismatch++; if (firstNull == "") firstNull = path + "." + fi.Name + " (" + fi.FieldType.Name + ")"; continue; }
        if (xv != null) Cmp(xv, yv, path + "." + fi.Name, depth + 1);
    }
};
Cmp(src, dup, "doc", 0);
SB.Append("null-where-source-had-a-value: ").Append(nullMismatch).Append(firstNull == "" ? "" : " first=" + firstNull).Append('\n');
SB.Append("layers src/dup = ").Append(src.layers.Count).Append('/').Append(dup.layers.Count)
  .Append("  dup layer3 hosted form = ")
  .Append(((Laubrary.PyreShaper.PyreFormCompositeSource)dup.layers[2].root.composite.source).form?.GetType().Name).Append('\n');
return SB.ToString();
