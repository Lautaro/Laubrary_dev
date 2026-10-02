// T-0337 §2.1 — for each zero-pixel dial on the six forms that has NO reason on the card, find whether ONE
// named sibling on its own owner opens it.
//
// HARD BOUNDS, because round 20's unbounded version of this pinned the editor for 75 minutes:
//   • one form per eval (pref T337.form);
//   • a wall-clock budget that stops the walk mid-way and says so;
//   • at most SIBLINGS_CAP siblings tried per dial, first-hit wins;
//   • every sibling AND the dial itself restored after each try (round 20's version leaked a ZUIValue and
//     drifted the form into ever more expensive states — that is what made it unbounded in practice);
//   • the dial list is read from a pref, not rediscovered, so the work is known before it starts.
var sb = new System.Text.StringBuilder();
const int W = 64, H = 64, N = 8, SEED = 1234567;
const int SIBLINGS_CAP = 26;
long BUDGET_MS = 150000;
var sw = System.Diagnostics.Stopwatch.StartNew();
bool truncated = false;

var BFi = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
var refT = ZType("ZuiReflect");
var fieldsOfM = refT.GetMethod("FieldsOf", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
var rampI = ZType("IZuiRamp");
var srcT = ZType("PyreFormCompositeSource");
var renderM = srcT.GetMethod("Render", BFi);
System.Func<System.Type, System.Reflection.FieldInfo[]> fieldsOf = t => fieldsOfM.Invoke(null, new object[] { t }) as System.Reflection.FieldInfo[];

string formName = UnityEditor.EditorPrefs.GetString("T337.form", "InfernoForm");
// "path|path|path" — the dotted paths d6 measured at 0 pixels with no reason on the card
string[] paths = UnityEditor.EditorPrefs.GetString("T337.paths", "").Split('|');

var form = System.Activator.CreateInstance(ZType(formName));
var src = System.Activator.CreateInstance(srcT);
srcT.GetField("form", BFi).SetValue(src, form);
srcT.GetField("frames", BFi).SetValue(src, N);

System.Func<float, UnityEngine.Color32[]> render = ph => { var b = new UnityEngine.Color32[W * H]; renderM.Invoke(src, new object[] { W, H, ph, (uint)SEED, b }); return b; };
System.Func<UnityEngine.Color32[], UnityEngine.Color32[], int> diff = (a, b) => { int n = 0; for (int i = 0; i < a.Length; i++) if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a) n++; return n; };

// resolve a dotted path to (owner, FieldInfo)
System.Func<string, object[]> resolve = path => {
    object o = form; var parts = path.Split('.');
    for (int i = 0; i < parts.Length; i++)
    {
        string p = parts[i]; int idx = -1;
        int br = p.IndexOf('[');
        if (br >= 0) { idx = int.Parse(p.Substring(br + 1, p.IndexOf(']') - br - 1)); p = p.Substring(0, br); }
        System.Reflection.FieldInfo f = null;
        foreach (var q in fieldsOf(o.GetType())) if (q.Name == p) { f = q; break; }
        if (f == null) return null;
        if (i == parts.Length - 1 && idx < 0) return new object[] { o, f };
        object nx = f.GetValue(o);
        if (idx >= 0) { var lst = nx as System.Collections.IList; if (lst == null || idx >= lst.Count) return null; nx = lst[idx]; }
        if (nx == null) return null;
        o = nx;
    }
    return null;
};

// set / get any numeric-ish member; returns a token to restore with
System.Func<object, System.Reflection.FieldInfo, object> snap = (o, f) => f.GetValue(o);
System.Action<object, System.Reflection.FieldInfo, object> back = (o, f, v) => f.SetValue(o, v);
System.Func<object, System.Reflection.FieldInfo, float, bool> bump = (o, f, v) => {
    var t = f.FieldType;
    try {
        if (t == typeof(float)) { f.SetValue(o, v); return true; }
        if (t == typeof(int)) { f.SetValue(o, (int)v); return true; }
        if (t == typeof(bool)) { f.SetValue(o, v != 0f); return true; }
        if (t.Name == "ZUIValue") {
            var zv = f.GetValue(o); if (zv == null) return false;
            var svP = t.GetProperty("staticValue", BFi); var mdP = t.GetProperty("mode", BFi);
            mdP.SetValue(zv, System.Enum.Parse(mdP.PropertyType, "Static"));
            svP.SetValue(zv, v); return true;
        }
    } catch { }
    return false;
};
// a ZUIValue is a reference — snapshot its scalar, not the object
System.Func<object, System.Reflection.FieldInfo, object[]> snapAny = (o, f) => {
    if (f.FieldType.Name == "ZUIValue") {
        var zv = f.GetValue(o); if (zv == null) return null;
        var svP = f.FieldType.GetProperty("staticValue", BFi); var mdP = f.FieldType.GetProperty("mode", BFi);
        return new object[] { "z", zv, svP.GetValue(zv), mdP.GetValue(zv) };
    }
    return new object[] { "v", f.GetValue(o) };
};
System.Action<object, System.Reflection.FieldInfo, object[]> backAny = (o, f, s) => {
    if (s == null) return;
    if ((string)s[0] == "z") {
        var svP = f.FieldType.GetProperty("staticValue", BFi); var mdP = f.FieldType.GetProperty("mode", BFi);
        svP.SetValue(s[1], s[2]); mdP.SetValue(s[1], s[3]);
    } else f.SetValue(o, s[1]);
};

int done = 0, opened = 0, still = 0, unresolved = 0;
foreach (var path in paths)
{
    if (string.IsNullOrEmpty(path)) continue;
    if (sw.ElapsedMilliseconds > BUDGET_MS) { truncated = true; break; }
    var r = resolve(path);
    if (r == null) { sb.Append(formName).Append('\t').Append(path).Append("\tUNRESOLVED\t\t\n"); unresolved++; continue; }
    object owner = r[0]; var dial = (System.Reflection.FieldInfo)r[1];

    var basis = new UnityEngine.Color32[N][];
    for (int i = 0; i < N; i++) basis[i] = render((float)i / (N - 1));
    System.Func<int> move = () => { int m = 0; for (int i = 0; i < N; i++) { int d = diff(basis[i], render((float)i / (N - 1))); if (d > m) m = d; } return m; };

    // baseline: does the dial itself move anything right now?
    var ds = snapAny(owner, dial);
    bool bumped = bump(owner, dial, dial.FieldType == typeof(bool) ? 1f : 2f);
    int baseMove = bumped ? move() : -1;
    backAny(owner, dial, ds);
    if (baseMove > 0)
    {
        sb.Append(formName).Append('\t').Append(path).Append("\tMOVES-ALREADY\t\t").Append(baseMove).Append('\n');
        done++; opened++; continue;
    }

    // bounded sibling search
    string found = ""; int foundPx = 0; int tried = 0;
    foreach (var sib in fieldsOf(owner.GetType()))
    {
        if (sw.ElapsedMilliseconds > BUDGET_MS) { truncated = true; break; }
        if (sib.Name == dial.Name) continue;
        var st = sib.FieldType;
        if (!(st == typeof(float) || st == typeof(int) || st == typeof(bool) || st.Name == "ZUIValue")) continue;
        if (tried++ >= SIBLINGS_CAP) break;
        var ss = snapAny(owner, sib);
        if (ss == null) continue;
        float target = st == typeof(bool) ? 1f : (st == typeof(int) ? 3f : 1f);
        if (!bump(owner, sib, target)) { backAny(owner, sib, ss); continue; }
        // now re-baseline WITH the sibling open, then bump the dial
        var b2 = new UnityEngine.Color32[N][];
        for (int i = 0; i < N; i++) b2[i] = render((float)i / (N - 1));
        var ds2 = snapAny(owner, dial);
        if (bump(owner, dial, dial.FieldType == typeof(bool) ? 1f : 2f))
        {
            int m = 0; for (int i = 0; i < N; i++) { int d = diff(b2[i], render((float)i / (N - 1))); if (d > m) m = d; }
            if (m > 0) { found = sib.Name; foundPx = m; }
        }
        backAny(owner, dial, ds2);
        backAny(owner, sib, ss);
        if (found.Length > 0) break;
    }
    done++;
    if (found.Length > 0) { opened++; sb.Append(formName).Append('\t').Append(path).Append("\tOPENED-BY\t").Append(found).Append('\t').Append(foundPx).Append('\n'); }
    else { still++; sb.Append(formName).Append('\t').Append(path).Append("\tNO-SINGLE-SIBLING\t\t0 (tried ").Append(tried).Append(" siblings)\n"); }
}

string outp = @"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0337\out\guard-search.tsv";
if (!System.IO.File.Exists(outp)) System.IO.File.WriteAllText(outp, "form\tpath\tverdict\topenedBy\tpixels\n");
System.IO.File.AppendAllText(outp, sb.ToString());
string res = formName + ": examined=" + done + " openedBySomething=" + opened + " stillDead=" + still
           + " unresolved=" + unresolved + " truncated=" + truncated + " elapsed=" + sw.ElapsedMilliseconds + "ms";
System.IO.File.AppendAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0337\out\guard-search-totals.txt", res + "\n");
return res;
