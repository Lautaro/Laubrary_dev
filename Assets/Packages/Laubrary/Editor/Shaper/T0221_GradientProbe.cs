// TEMP PROBE T-0221 — PM deletes after running
//
// Proves the unlimited-stop ZuiGradient did not move a single pixel:
//   A  a pre-stop-list gradient evaluates identically through its stop list as through its legacy Gradient
//   B  every Pyre ramp preset survives the ZuiGradient conversion and the ramp↔gradient bridge EXACTLY
//   C  a saved-library entry round-trips at 12 stops (the old 8-key cap is gone)
//   D  the demo document renders to the same hash before and after its gradients are converted
//   E  one authored Pyre asset does the same
//
// D and E work on Instantiate() COPIES, so no authored asset is touched, dirtied or saved.
// Call: T0221_GradientProbe.RunAll() — returns the report as a string.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Laubrary.Pyre;
using Laubrary.PyreShaper;
using Laubrary.Shaper;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;

public static class T0221_GradientProbe
{
    const int Samples = 1024;

    public static string RunAll()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== T-0221 gradient probe ===");
        Section(sb, "A migration exactness", A_Migration);
        Section(sb, "B Pyre ramp presets", B_Presets);
        Section(sb, "C saved-library round-trip", C_Library);
        Section(sb, "D Shaper demo document render", D_ShaperDoc);
        Section(sb, "E Pyre asset render", E_PyreAsset);
        return sb.ToString();
    }

    static void Section(StringBuilder sb, string title, Action<StringBuilder> body)
    {
        sb.AppendLine("--- " + title + " ---");
        try { body(sb); }
        catch (Exception e) { sb.AppendLine("FAIL (exception): " + e); }
        sb.AppendLine();
    }

    // ── A: an un-converted gradient must evaluate the same either way ────────────────────────────────

    static void A_Migration(StringBuilder sb)
    {
        var cases = new List<(string name, Gradient g)>
        {
            ("2 keys", Keys(new[] { (0f, Color.black), (1f, Color.white) }, new[] { (0f, 1f), (1f, 1f) })),
            ("alpha keys off the colour keys", Keys(
                new[] { (0f, Color.red), (0.5f, Color.green), (1f, Color.blue) },
                new[] { (0f, 0f), (0.3f, 1f), (0.77f, 0.25f), (1f, 1f) })),
            ("8 keys", Keys(new[]
            {
                (0f, Color.black), (0.14f, Color.red), (0.28f, new Color(1f, 0.5f, 0f)), (0.42f, Color.yellow),
                (0.57f, Color.green), (0.71f, Color.cyan), (0.85f, Color.blue), (1f, Color.white),
            }, new[] { (0f, 1f), (1f, 1f) })),
        };
        var stepped = Keys(new[] { (0f, Color.red), (0.4f, Color.green), (0.8f, Color.blue) },
                           new[] { (0f, 1f), (1f, 1f) });
        stepped.mode = GradientMode.Fixed;
        cases.Add(("fixed/stepped", stepped));

        foreach (var (name, g) in cases)
        {
            var zg = Unmigrated(g);
            var before = new Color[Samples];
            for (int i = 0; i < Samples; i++) before[i] = zg.EvalRamp(T(i));
            int stopsBefore = CountStops(zg);
            zg.EnsureStops();
            float maxDelta = 0f; int byteMismatch = 0;
            for (int i = 0; i < Samples; i++)
            {
                var a = before[i]; var b = zg.EvalRamp(T(i));
                maxDelta = Mathf.Max(maxDelta, Delta(a, b));
                if (!SameBytes(a, b)) byteMismatch++;
            }
            sb.AppendLine($"{Verdict(byteMismatch == 0)} {name}: {stopsBefore}→{zg.Count} stops, "
                        + $"max channel delta {maxDelta:0.######}, 8-bit mismatches {byteMismatch}/{Samples}");
        }
    }

    // ── B: every shipped Pyre ramp preset, through the conversion and back ───────────────────────────

    static void B_Presets(StringBuilder sb)
    {
        int total = 0, exact = 0, evalExact = 0, bridgeExact = 0, over8 = 0;
        var worst = "";
        float worstDelta = 0f;

        foreach (var preset in PyreShaperRampPresets.All)
        {
            var ramp = preset.factory();
            if (ramp == null || ramp.stops == null || ramp.stops.Count == 0) continue;
            total++;
            if (ramp.stops.Count > 8) over8++;

            var zg = PyreShaperRampPresets.ToZuiGradient(ramp);
            bool stopsMatch = zg != null && zg.Count == ramp.stops.Count;
            if (stopsMatch)
                for (int i = 0; i < zg.Count; i++)
                    stopsMatch &= Mathf.Approximately(zg.GetPos(i), Sorted(ramp)[i].pos)
                               && SameBytes(zg.GetColor(i), Sorted(ramp)[i].color);
            if (stopsMatch) exact++;

            // The gradient must EVALUATE the Pyre ramp, blend space included.
            float maxDelta = 0f;
            for (int i = 0; i < Samples; i++)
            {
                float t = T(i);
                maxDelta = Mathf.Max(maxDelta, Delta(ramp.Evaluate(t), zg.EvalRamp(t)));
            }
            if (maxDelta <= 1e-6f) evalExact++;
            if (maxDelta > worstDelta) { worstDelta = maxDelta; worst = preset.name; }

            // …and the bridge must put it back on a ramp stop-for-stop, space included.
            var back = new PyreRamp();
            ZuiRampGradientBridge.ApplyZuiGradient(back, zg);
            bool bridgeOk = back.stops.Count == ramp.stops.Count && back.space == ramp.space;
            if (bridgeOk)
                for (int i = 0; i < back.stops.Count; i++)
                    bridgeOk &= Mathf.Approximately(back.stops[i].pos, Sorted(ramp)[i].pos)
                             && SameBytes(back.stops[i].color, Sorted(ramp)[i].color);
            if (bridgeOk) bridgeExact++;
        }

        sb.AppendLine($"{Verdict(exact == total)} stop-for-stop conversion: {exact}/{total} presets "
                    + $"({over8} of them have more than 8 stops)");
        sb.AppendLine($"{Verdict(evalExact == total)} evaluation matches PyreShade: {evalExact}/{total} "
                    + $"(worst: {worst} at {worstDelta:0.######})");
        sb.AppendLine($"{Verdict(bridgeExact == total)} ramp→gradient→ramp round-trip: {bridgeExact}/{total}");
    }

    static List<PyreRampStop> Sorted(PyreRamp r)
    {
        var s = new List<PyreRampStop>(r.stops);
        s.Sort((a, b) => a.pos.CompareTo(b.pos));
        return s;
    }

    // ── C: the saved library keeps every stop ────────────────────────────────────────────────────────

    static void C_Library(StringBuilder sb)
    {
        var src = new ZuiGradient();
        var stops = src.Stops;
        stops.Clear();
        for (int i = 0; i < 12; i++)
            stops.Add(new ZuiGradientStop(i / 11f, Color.HSVToRGB(i / 12f, 1f, 1f)));
        src.MarkStopsChanged();
        src.BlendMode = 0;   // Linear Light

        // The library's own entry shape, without writing the project asset.
        var entry = new ZuiGradientPresetEntry { name = "probe", space = (ZuiGradientSpace)src.BlendMode };
        foreach (var s in src.Stops) entry.stops.Add(new ZuiGradientStop(s.pos, s.color));
        var back = entry.Resolve();

        bool ok = back.Count == 12 && back.BlendMode == src.BlendMode;
        for (int i = 0; ok && i < 12; i++)
            ok = Mathf.Approximately(back.GetPos(i), src.GetPos(i)) && SameBytes(back.GetColor(i), src.GetColor(i));
        sb.AppendLine($"{Verdict(ok)} 12-stop entry saved and resolved: {back.Count} stops back, "
                    + $"space {(ZuiGradientSpace)back.BlendMode}");

        // And the deliberate lossy boundary is still only the Gradient EXPORT.
        var exported = src.gradient;
        sb.AppendLine($"info  Gradient export of the same ramp: {exported.colorKeys.Length} keys "
                    + "(the 8-key cap now applies only where a UnityEngine.Gradient is demanded)");
    }

    // ── D / E: real renders, before and after the conversion ─────────────────────────────────────────

    static void D_ShaperDoc(StringBuilder sb)
    {
        var doc = AssetDatabase.LoadAssetAtPath<ShaperDocument>("Assets/Demos/ShaperDemo/ShaperDemoDoc.asset");
        if (doc == null) { sb.AppendLine("skip  ShaperDemoDoc.asset not found"); return; }

        var copy = UnityEngine.Object.Instantiate(doc);   // never touch the authored asset
        try
        {
            int frames = Mathf.Clamp(copy.frameCount, 1, 8);
            ulong before = HashFrames(f => ShaperDocumentRenderer.RenderFrame(copy, f), frames);
            var found = new List<ZuiGradient>();
            Collect(copy, found, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
            int unconverted = 0;
            foreach (var g in found) { if (CountStops(g) == 0) unconverted++; g.EnsureStops(); }
            ulong after = HashFrames(f => ShaperDocumentRenderer.RenderFrame(copy, f), frames);
            sb.AppendLine($"{Verdict(before == after)} {frames} frames, {found.Count} gradients "
                        + $"({unconverted} converted by this run): hash {before:X16} → {after:X16}");
        }
        finally { UnityEngine.Object.DestroyImmediate(copy); }
    }

    static void E_PyreAsset(StringBuilder sb)
    {
        var guids = AssetDatabase.FindAssets("t:Pyre");
        if (guids == null || guids.Length == 0) { sb.AppendLine("skip  no Pyre asset in the project"); return; }

        int done = 0;
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var spec = AssetDatabase.LoadAssetAtPath<Pyre>(path);
            if (spec == null) continue;

            var copy = UnityEngine.Object.Instantiate(spec);
            try
            {
                int frames = Mathf.Clamp(copy.frameCount, 1, 4);
                ulong before = HashFrames(f => PyreRenderer.RenderFrame(copy, f), frames);
                var found = new List<ZuiGradient>();
                Collect(copy, found, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
                foreach (var g in found) g.EnsureStops();
                ulong after = HashFrames(f => PyreRenderer.RenderFrame(copy, f), frames);
                sb.AppendLine($"{Verdict(before == after)} {System.IO.Path.GetFileNameWithoutExtension(path)}: "
                            + $"{frames} frames, {found.Count} gradients, hash {before:X16} → {after:X16}");
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
            if (++done >= 3) break;   // three assets is a sample, not a sweep
        }
    }

    static ulong HashFrames(Func<int, Color32[]> render, int frames)
    {
        ulong h = 14695981039346656037UL;
        for (int f = 0; f < frames; f++)
        {
            var px = render(f);
            if (px == null) continue;
            foreach (var c in px)
            {
                h = Mix(h, c.r); h = Mix(h, c.g); h = Mix(h, c.b); h = Mix(h, c.a);
            }
        }
        return h;
    }

    static ulong Mix(ulong h, byte b) { h ^= b; return h * 1099511628211UL; }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────

    static float T(int i) => i / (float)(Samples - 1);
    static string Verdict(bool ok) => ok ? "PASS " : "FAIL ";
    static float Delta(Color a, Color b) =>
        Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Max(Mathf.Abs(a.b - b.b), Mathf.Abs(a.a - b.a))));
    static bool SameBytes(Color a, Color b)
    {
        Color32 x = a, y = b;
        return x.r == y.r && x.g == y.g && x.b == y.b && x.a == y.a;
    }

    static Gradient Keys((float t, Color c)[] ck, (float t, float a)[] ak)
    {
        var g = new Gradient();
        var c = new GradientColorKey[ck.Length];
        var a = new GradientAlphaKey[ak.Length];
        for (int i = 0; i < ck.Length; i++) c[i] = new GradientColorKey(ck[i].c, ck[i].t);
        for (int i = 0; i < ak.Length; i++) a[i] = new GradientAlphaKey(ak[i].a, ak[i].t);
        g.SetKeys(c, a);
        return g;
    }

    /// A ZuiGradient in the state an asset written before T-0221 loads in: a legacy Gradient, no stops.
    static ZuiGradient Unmigrated(Gradient g)
    {
        var zg = new ZuiGradient();
        Field("legacyGradient").SetValue(zg, g);
        Field("stopsAuthored").SetValue(zg, false);
        ((List<ZuiGradientStop>)Field("stops").GetValue(zg)).Clear();
        return zg;
    }

    static int CountStops(ZuiGradient g) => ((List<ZuiGradientStop>)Field("stops").GetValue(g)).Count;

    static FieldInfo Field(string name) =>
        typeof(ZuiGradient).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    /// Every ZuiGradient reachable from `root` by serializable fields (plain classes and lists recursed,
    /// UnityEngine.Object references not followed — those are separate assets).
    static void Collect(object root, List<ZuiGradient> found, HashSet<object> seen, int depth)
    {
        if (root == null || depth > 8) return;
        if (root is ZuiGradient zg) { if (seen.Add(zg)) found.Add(zg); return; }
        if (root is string || root is UnityEngine.Object && depth > 0) return;
        if (!seen.Add(root)) return;

        if (root is IList list)
        {
            foreach (var e in list) if (e != null && !(e is ValueType)) Collect(e, found, seen, depth + 1);
            return;
        }

        var t = root.GetType();
        if (t.IsPrimitive || t.IsEnum) return;
        foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var ft = f.FieldType;
            if (ft.IsPrimitive || ft.IsEnum || ft == typeof(string)) continue;
            object v;
            try { v = f.GetValue(root); } catch { continue; }
            if (v == null) continue;
            if (v is ValueType && !(v is ZuiGradient)) continue;
            Collect(v, found, seen, depth + 1);
        }
    }

    sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
        public new bool Equals(object a, object b) => ReferenceEquals(a, b);
        public int GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
    }
}
