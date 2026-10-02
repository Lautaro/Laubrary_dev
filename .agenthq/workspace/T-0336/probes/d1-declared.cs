// T-0336 §2 — hosted dials AS DECLARED, not as drawn.
//
// For each of the three largest hosted forms: enumerate EVERY field ZuiReflect.FieldsOf would hand the
// drawer, recursing into nested [Serializable] settings objects exactly as ZuiReflect does, resolve every
// [ZUIShowIf] gate by DRIVING the gate to each of its values, and for each declared dial measure how many
// pixels it moves through the composite source's own Render — the same call Shaper's preview makes.
//
// Output: out/declared-<form>.tsv  columns: form, path, type, gateField, gateValueUsed, visible, pixels, valueTried, note
var sb = new System.Text.StringBuilder();
const int W = 64, H = 64, N = 8, SEED = 1234567;
var BFi = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
var refT = ZType("ZuiReflect");
var fieldsOfM = refT.GetMethod("FieldsOf", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
var showT = ZType("ZUIShowIfAttribute");
var swarmOnlyT = ZType("PyreSwarmOnlyAttribute");
var rampI = ZType("IZuiRamp");
var srcT = ZType("PyreFormCompositeSource");
var renderM = srcT.GetMethod("Render", BFi);

System.Func<System.Type, System.Reflection.FieldInfo[]> fieldsOf = t => fieldsOfM.Invoke(null, new object[] { t }) as System.Reflection.FieldInfo[];
System.Func<System.Reflection.FieldInfo, string[]> gateOf = f => {
    var a = System.Attribute.GetCustomAttribute(f, showT); if (a == null) return null;
    var gf = a.GetType().GetProperty("Field").GetValue(a) as string;
    var vs = a.GetType().GetProperty("Values").GetValue(a) as string[];
    var r = new string[1 + (vs == null ? 0 : vs.Length)]; r[0] = gf;
    for (int i = 0; vs != null && i < vs.Length; i++) r[i + 1] = vs[i];
    return r;
};

// a leaf = something ZuiReflect draws as ONE control; anything else with serializable fields is recursed into
System.Func<System.Type, bool> isLeaf = t =>
    t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal)
    || t == typeof(UnityEngine.Color) || t == typeof(UnityEngine.Color32)
    || t == typeof(UnityEngine.Vector2) || t == typeof(UnityEngine.Vector3) || t == typeof(UnityEngine.Vector4)
    || t == typeof(UnityEngine.Rect) || t == typeof(UnityEngine.AnimationCurve) || t == typeof(UnityEngine.Gradient)
    || t.Name == "ZUIValue" || t.Name == "ZuiGradient"
    || (rampI != null && rampI.IsAssignableFrom(t))
    || typeof(UnityEngine.Object).IsAssignableFrom(t);

string[] names = { "ArcBurstForm", "PlasmaBloomForm", "ForkBlastForm" };
var totals = new System.Text.StringBuilder();

foreach (var formName in names)
{
    var ft = ZType(formName); if (ft == null) { sb.Append(formName).Append("\tNO TYPE\n"); continue; }
    var form = System.Activator.CreateInstance(ft);
    var src = System.Activator.CreateInstance(srcT);
    srcT.GetField("form", BFi).SetValue(src, form);
    srcT.GetField("frames", BFi).SetValue(src, N);

    System.Func<float, UnityEngine.Color32[]> render = ph => {
        var buf = new UnityEngine.Color32[W * H];
        renderM.Invoke(src, new object[] { W, H, ph, (uint)SEED, buf });
        return buf;
    };
    System.Func<UnityEngine.Color32[], UnityEngine.Color32[], int> diff = (a, b) => {
        int n = 0; for (int i = 0; i < a.Length; i++) if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a) n++; return n;
    };

    int declared = 0, reachable = 0, moved = 0, skipped = 0, never = 0, inertZero = 0;

    // ── recursive walk ────────────────────────────────────────────────────────────────────────────────
    System.Action<object, string, int> walk = null;
    walk = (owner, prefix, depth) =>
    {
        if (owner == null || depth > 4) return;
        var all = fieldsOf(owner.GetType());
        // gate domains for this owner
        var gateNames = new System.Collections.Generic.HashSet<string>();
        foreach (var f in all) { var g = gateOf(f); if (g != null) gateNames.Add(g[0]); }

        foreach (var f in all)
        {
            string path = prefix + f.Name;
            bool swarmOnly = swarmOnlyT != null && System.Attribute.IsDefined(f, swarmOnlyT);
            var g = gateOf(f);
            string gateField = g == null ? "" : g[0];
            System.Reflection.FieldInfo gateFi = null;
            if (g != null) foreach (var q in all) if (q.Name == g[0]) { gateFi = q; break; }

            if (swarmOnly)
            {
                skipped++;
                sb.Append(formName).Append('\t').Append(path).Append('\t').Append(f.FieldType.Name).Append('\t')
                  .Append(gateField).Append('\t').Append('\t').Append("ABSENT").Append('\t').Append(0).Append('\t')
                  .Append('\t').Append("[PyreSwarmOnly] — deliberately absented by PyreFormShaperUI's Skip (a composite hosts no swarm)").Append('\n');
                continue;
            }
            declared++;

            // drive the gate to a value that shows this field
            object savedGate = null; bool gateDriven = false; string gateUsed = "";
            if (gateFi != null && g.Length > 1)
            {
                savedGate = gateFi.GetValue(owner);
                object target = null;
                for (int i = 1; i < g.Length && target == null; i++)
                {
                    try {
                        if (gateFi.FieldType.IsEnum) target = System.Enum.Parse(gateFi.FieldType, g[i], true);
                        else if (gateFi.FieldType == typeof(bool)) target = bool.Parse(g[i]);
                        else if (gateFi.FieldType == typeof(int)) target = int.Parse(g[i]);
                        else if (gateFi.FieldType == typeof(float)) target = float.Parse(g[i]);
                    } catch { target = null; }
                }
                if (target != null) { gateFi.SetValue(owner, target); gateDriven = true; gateUsed = target.ToString(); }
            }
            else if (gateFi == null && g != null)
            {
                gateUsed = "(gate field '" + g[0] + "' NOT FOUND — ZuiReflect fails OPEN, so the dial shows)";
            }

            bool vis = true;
            if (gateFi != null && g != null && g.Length > 1)
            {
                string cur = gateFi.GetValue(owner) == null ? null : gateFi.GetValue(owner).ToString();
                vis = false; for (int i = 1; i < g.Length; i++) if (string.Equals(g[i], cur, System.StringComparison.OrdinalIgnoreCase)) { vis = true; break; }
            }
            if (!vis)
            {
                never++;
                sb.Append(formName).Append('\t').Append(path).Append('\t').Append(f.FieldType.Name).Append('\t')
                  .Append(gateField).Append('\t').Append(gateUsed).Append('\t').Append("NEVER-VISIBLE").Append('\t').Append(0).Append('\t').Append('\t')
                  .Append("no value of the gate makes this dial show").Append('\n');
                if (gateDriven) gateFi.SetValue(owner, savedGate);
                continue;
            }
            reachable++;

            var t = f.FieldType;
            if (!isLeaf(t) && t.IsClass && !t.IsAbstract && !typeof(UnityEngine.Object).IsAssignableFrom(t)
                && !(t.IsGenericType && t.GetGenericTypeDefinition() == typeof(System.Collections.Generic.List<>)))
            {
                var child = f.GetValue(owner);
                if (child == null) { try { child = System.Activator.CreateInstance(t); f.SetValue(owner, child); } catch { } }
                sb.Append(formName).Append('\t').Append(path).Append('\t').Append(t.Name).Append('\t')
                  .Append(gateField).Append('\t').Append(gateUsed).Append('\t').Append("CONTAINER").Append('\t').Append(0).Append('\t').Append('\t')
                  .Append("nested settings object — its own dials follow").Append('\n');
                declared--; reachable--;   // a container is not itself a dial
                walk(child, path + ".", depth + 1);
                if (gateDriven) gateFi.SetValue(owner, savedGate);
                continue;
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(System.Collections.Generic.List<>))
            {
                var lst = f.GetValue(owner) as System.Collections.IList;
                var et = t.GetGenericArguments()[0];
                sb.Append(formName).Append('\t').Append(path).Append('\t').Append("List<" + et.Name + ">").Append('\t')
                  .Append(gateField).Append('\t').Append(gateUsed).Append('\t').Append("LIST").Append('\t').Append(0).Append('\t')
                  .Append(lst == null ? 0 : lst.Count).Append('\t').Append("reorderable list of " + et.Name).Append('\n');
                declared--; reachable--;
                if (lst != null && !isLeaf(et)) for (int i = 0; i < lst.Count && i < 2; i++) walk(lst[i], path + "[" + i + "].", depth + 1);
                if (gateDriven) gateFi.SetValue(owner, savedGate);
                continue;
            }

            // ── a leaf dial: perturb it and count pixels ──────────────────────────────────────────────
            var b0 = render(0f); var bM = render(0.5f); var bL = render(1f);
            System.Func<System.Action, string, int> probe = (mutate, label) => {
                try {
                    mutate();
                    int d = System.Math.Max(diff(bM, render(0.5f)), System.Math.Max(diff(b0, render(0f)), diff(bL, render(1f))));
                    return d;
                } catch { return -1; }
            };
            int best = 0; string bestVal = "";
            float[] cands = { 0f, 0.25f, 0.5f, 1f, 2f, 4f, 8f, 16f, 64f, -1f, 0.05f };
            if (t.Name == "ZUIValue")
            {
                var v = f.GetValue(owner); if (v == null) { v = System.Activator.CreateInstance(t, new object[] { 0f }); f.SetValue(owner, v); }
                var svP = t.GetProperty("staticValue", BFi); var mdP = t.GetProperty("mode", BFi);
                var om = mdP.GetValue(v); float ov = (float)svP.GetValue(v);
                mdP.SetValue(v, System.Enum.Parse(mdP.PropertyType, "Static"));
                foreach (var c in cands) { if (UnityEngine.Mathf.Approximately(c, ov)) continue; var cc = c;
                    int d = probe(() => svP.SetValue(v, cc), cc.ToString()); if (d > best) { best = d; bestVal = cc.ToString(); } if (best > 0) break; }
                mdP.SetValue(v, om); svP.SetValue(v, ov);
            }
            else if (t == typeof(float))
            {
                float ov = (float)f.GetValue(owner);
                foreach (var c in cands) { if (UnityEngine.Mathf.Approximately(c, ov)) continue; var cc = c;
                    int d = probe(() => f.SetValue(owner, cc), cc.ToString()); if (d > best) { best = d; bestVal = cc.ToString(); } if (best > 0) break; }
                f.SetValue(owner, ov);
            }
            else if (t == typeof(int))
            {
                int ov = (int)f.GetValue(owner);
                int[] ic = { 0, 1, 2, 3, 5, 8, 16 };
                foreach (var c in ic) { if (c == ov) continue; var cc = c;
                    int d = probe(() => f.SetValue(owner, cc), cc.ToString()); if (d > best) { best = d; bestVal = cc.ToString(); } if (best > 0) break; }
                f.SetValue(owner, ov);
            }
            else if (t == typeof(bool))
            {
                bool ov = (bool)f.GetValue(owner);
                best = probe(() => f.SetValue(owner, !ov), (!ov).ToString()); bestVal = (!ov).ToString();
                f.SetValue(owner, ov);
            }
            else if (t.IsEnum)
            {
                var ov = f.GetValue(owner); int tried = 0;
                foreach (var vv in System.Enum.GetValues(t)) { if (vv.Equals(ov)) continue; if (tried++ >= 6) break; var cap = vv;
                    int d = probe(() => f.SetValue(owner, cap), cap.ToString()); if (d > best) { best = d; bestVal = cap.ToString(); } if (best > 0) break; }
                f.SetValue(owner, ov);
            }
            else if (t == typeof(UnityEngine.Color))
            {
                var ov = (UnityEngine.Color)f.GetValue(owner);
                best = probe(() => f.SetValue(owner, new UnityEngine.Color(1f, 0f, 1f, 1f)), "magenta"); bestVal = "magenta";
                if (best == 0) { best = probe(() => f.SetValue(owner, new UnityEngine.Color(0f, 1f, 0f, 0f)), "green a=0"); bestVal = "green a=0"; }
                f.SetValue(owner, ov);
            }
            else if (t == typeof(UnityEngine.Vector2))
            {
                var ov = (UnityEngine.Vector2)f.GetValue(owner);
                best = probe(() => f.SetValue(owner, ov + new UnityEngine.Vector2(8f, 8f)), "+8,+8"); bestVal = "+8,+8";
                f.SetValue(owner, ov);
            }
            else if (t.Name == "ZuiGradient")
            {
                var zg = f.GetValue(owner);
                System.Collections.IList stops = null;
                if (zg != null) {
                    var sf = t.GetField("stops", BFi); if (sf != null) stops = sf.GetValue(zg) as System.Collections.IList;
                    if (stops == null) { var sp = t.GetProperty("stops", BFi); if (sp != null) stops = sp.GetValue(zg) as System.Collections.IList; }
                }
                if (stops != null && stops.Count > 0)
                {
                    var st = stops[0];
                    var cF = st.GetType().GetField("color", BFi);
                    var cP = cF == null ? st.GetType().GetProperty("color", BFi) : null;
                    if (cF != null || cP != null) {
                        object oc = cF != null ? cF.GetValue(st) : cP.GetValue(st);
                        best = probe(() => { if (cF != null) cF.SetValue(st, UnityEngine.Color.magenta); else cP.SetValue(st, UnityEngine.Color.magenta); }, "stop0=magenta");
                        bestVal = "stop0=magenta";
                        if (cF != null) cF.SetValue(st, oc); else cP.SetValue(st, oc);
                    } else bestVal = "(stop has no colour member)";
                }
                else bestVal = "(no stops)";
            }
            else if (rampI != null && rampI.IsAssignableFrom(t))
            {
                // a PyreRamp — perturb the first thing on it that is a Color or a Gradient
                var rv = f.GetValue(owner);
                if (rv != null) foreach (var rf in fieldsOf(rv.GetType()))
                {
                    if (rf.FieldType == typeof(UnityEngine.Color)) { var oc = rf.GetValue(rv);
                        int d = probe(() => rf.SetValue(rv, UnityEngine.Color.magenta), rf.Name + "=magenta");
                        if (d > best) { best = d; bestVal = rf.Name + "=magenta"; } rf.SetValue(rv, oc); if (best > 0) break; }
                    else if (rf.FieldType == typeof(float)) { var of2 = (float)rf.GetValue(rv);
                        int d = probe(() => rf.SetValue(rv, of2 + 1f), rf.Name + "+1");
                        if (d > best) { best = d; bestVal = rf.Name + "+1"; } rf.SetValue(rv, of2); if (best > 0) break; }
                    else if (rf.FieldType.Name == "ZUIValue") { var zv = rf.GetValue(rv);
                        if (zv == null) continue;
                        var svP2 = rf.FieldType.GetProperty("staticValue", BFi); if (svP2 == null) continue;
                        float o3 = (float)svP2.GetValue(zv);
                        int d = probe(() => svP2.SetValue(zv, o3 + 1f), rf.Name + "+1");
                        if (d > best) { best = d; bestVal = rf.Name + "+1"; } svP2.SetValue(zv, o3); if (best > 0) break; }
                }
                if (bestVal.Length == 0) bestVal = "(no perturbable member)";
            }
            else { bestVal = "(type not perturbed)"; best = -1; }

            if (best > 0) moved++; else if (best == 0) inertZero++;
            sb.Append(formName).Append('\t').Append(path).Append('\t').Append(t.Name).Append('\t')
              .Append(gateField).Append('\t').Append(gateUsed).Append('\t').Append("VISIBLE").Append('\t')
              .Append(best).Append('\t').Append(bestVal).Append('\t').Append('\n');

            if (gateDriven) gateFi.SetValue(owner, savedGate);
        }
    };

    walk(form, "", 0);
    totals.Append(formName).Append(": declared dials=").Append(declared)
          .Append(" reachable=").Append(reachable).Append(" movePixels=").Append(moved)
          .Append(" zeroPixels=").Append(inertZero).Append(" neverVisible=").Append(never)
          .Append(" absentedSwarmOnly=").Append(skipped).Append("\n");
}

System.IO.File.WriteAllText(@"D:\UNITY\Laubrary Dev\.agenthq\workspace\T-0336\out\declared-dials.tsv",
    "form\tpath\ttype\tgateField\tgateValueUsed\tvisible\tpixels\tvalueTried\tnote\n" + sb.ToString());
return totals.ToString();
