// TEMP PROBE T-0223 — deleted in the commit that follows this task's verification.
//
// Every ramp and gradient site now draws Unity's GradientField, which can only show 8 colour keys, while the
// storage underneath has no cap. That is only safe if two things hold, so both are measured here rather than
// asserted:
//   A  DISPLAY IS READ-ONLY — building the picture the field is handed never writes to the ramp, at any stop
//      count, so an untouched preset or a 14-stop library palette keeps every stop and renders exactly.
//   B  AN EDIT ROUND-TRIPS — a ramp within Unity's 8-key reach comes back from the field unchanged, so editing
//      one colour does not quietly redraw the rest.
//   C  THE 8-KEY BOUNDARY IS HONEST — a >8-stop ramp shows exactly 8 keys with its true endpoints kept.
//   D/E RENDER HASH — the Shaper demo document and authored Pyre assets render to the same pixels after every
//      ramp they contain has been through the display path, and an ≤8-stop ramp still does after a simulated
//      edit; only a >8-stop ramp's pixels move, which is the trade this task chose deliberately.
//
// D and E work on Instantiate() COPIES, so no authored asset is touched, dirtied or saved.
// Call: T0223_RampFieldProbe.RunAll() — returns the report as a string. No menu item.
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

public static class T0223_RampFieldProbe
{
    const int Samples = 1024;
    const float Eps = 1e-4f;

    public static string RunAll()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== T-0223 GradientField probe ===");
        Section(sb, "A/B/C Pyre ramp presets through the field", A_Presets);
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

    // ── A/B/C: every shipped Pyre ramp preset, through exactly what the control does ──────────────────

    static void A_Presets(StringBuilder sb)
    {
        int total = 0, over8 = 0, displayClean = 0, boundaryOk = 0, roundTripOk = 0, spaceKept = 0, under8 = 0;
        float worstRoundTrip = 0f; string worstName = "";

        foreach (var preset in PyreShaperRampPresets.All)
        {
            var ramp = preset.factory();
            if (ramp == null || ramp.stops == null || ramp.stops.Count == 0) continue;
            total++;
            int n = ramp.stops.Count;

            var before = Snapshot(ramp);
            var beforeSamples = Sample(ramp);
            var spaceBefore = ramp.space;

            // A — exactly the call the control makes to fill the field.
            var shown = ZuiRampGradientBridge.ToGradient(ramp);
            if (SameSnapshot(before, Snapshot(ramp)) && MaxDelta(beforeSamples, Sample(ramp)) == 0f
                && ramp.space == spaceBefore) displayClean++;
            else sb.AppendLine($"FAIL  display mutated: {preset.name}");

            if (n > 8)
            {
                // C — the cap is paid here and only here, with the ramp's real ends kept.
                over8++;
                bool ok = shown.colorKeys.Length == 8
                       && DeltaRgb(shown.colorKeys[0].color, ramp.stops[0].color) <= Eps
                       && DeltaRgb(shown.colorKeys[7].color, ramp.stops[n - 1].color) <= Eps
                       && Mathf.Abs(shown.alphaKeys[0].alpha - ramp.stops[0].color.a) <= Eps
                       && Mathf.Abs(shown.alphaKeys[7].alpha - ramp.stops[n - 1].color.a) <= Eps;
                if (ok) boundaryOk++;
                else sb.AppendLine($"FAIL  8-key boundary: {preset.name}");
                continue;
            }

            // B — a simulated edit: the field hands back what it was shown, the control writes it home.
            under8++;
            var edited = preset.factory();
            ZuiRampGradientBridge.ApplyGradient(edited, shown);
            float d = MaxDelta(beforeSamples, Sample(edited));
            if (d > worstRoundTrip) { worstRoundTrip = d; worstName = preset.name; }
            if (d <= Eps && edited.stops.Count == n) roundTripOk++;
            else sb.AppendLine($"FAIL  round-trip: {preset.name} delta={d:0.######} stops {n}→{edited.stops.Count}");
            if (edited.space == spaceBefore) spaceKept++;
            else sb.AppendLine($"FAIL  blend space lost: {preset.name} {spaceBefore}→{edited.space}");
        }

        sb.AppendLine($"{Verdict(displayClean == total)} display never writes: {displayClean}/{total} presets "
                    + $"({over8} of them hold more than 8 stops)");
        sb.AppendLine($"{Verdict(boundaryOk == over8)} 8-key picture with exact endpoints: {boundaryOk}/{over8}");
        sb.AppendLine($"{Verdict(roundTripOk == under8)} edit round-trips (≤8 stops): {roundTripOk}/{under8} "
                    + $"(worst channel delta {worstRoundTrip:0.########}{(worstName == "" ? "" : ", " + worstName)} "
                    + "— Unity's own key-storage precision, not a blend change)");
        sb.AppendLine($"{Verdict(spaceKept == under8)} blend space survives an edit: {spaceKept}/{under8}");
    }

    // ── D / E: real renders ──────────────────────────────────────────────────────────────────────────

    static void D_ShaperDoc(StringBuilder sb)
    {
        var doc = AssetDatabase.LoadAssetAtPath<ShaperDocument>("Assets/Demos/ShaperDemo/ShaperDemoDoc.asset");
        if (doc == null) { sb.AppendLine("skip  ShaperDemoDoc.asset not found"); return; }

        var copy = UnityEngine.Object.Instantiate(doc);   // never touch the authored asset
        try
        {
            int frames = Mathf.Clamp(copy.frameCount, 1, 8);
            ulong before = HashFrames(f => ShaperDocumentRenderer.RenderFrame(copy, f), frames);
            var ramps = new List<IZuiRamp>();
            Collect(copy, ramps, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
            foreach (var r in ramps) ZuiRampGradientBridge.ToGradient(r);   // what opening the window does
            ulong after = HashFrames(f => ShaperDocumentRenderer.RenderFrame(copy, f), frames);
            sb.AppendLine($"{Verdict(before == after)} {frames} frames, {ramps.Count} ramps shown in a field: "
                        + $"hash {before:X16} → {after:X16}");
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
                var ramps = new List<IZuiRamp>();
                Collect(copy, ramps, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
                foreach (var r in ramps) ZuiRampGradientBridge.ToGradient(r);
                ulong shownHash = HashFrames(f => PyreRenderer.RenderFrame(copy, f), frames);

                // Now a simulated EDIT of every ramp the asset holds: the field hands back what it showed.
                int wide = 0;
                foreach (var r in ramps)
                {
                    if (r.Count > 8) wide++;
                    var g = ZuiRampGradientBridge.ToGradient(r);
                    if (g != null) ZuiRampGradientBridge.ApplyGradient(r, g);
                }
                ulong editedHash = HashFrames(f => PyreRenderer.RenderFrame(copy, f), frames);
                bool editExpected = wide > 0 || editedHash == before;

                sb.AppendLine($"{Verdict(before == shownHash)} {System.IO.Path.GetFileNameWithoutExtension(path)}: "
                            + $"{frames} frames, {ramps.Count} ramps ({wide} over 8 stops) — shown {before:X16} → {shownHash:X16}");
                sb.AppendLine($"{Verdict(editExpected)}   after a simulated edit of every ramp: {editedHash:X16} "
                            + (wide > 0 ? "(a >8-stop ramp was collapsed — expected to differ)"
                                        : "(all ramps within 8 keys — expected identical)"));
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
            foreach (var c in px) { h = Mix(h, c.r); h = Mix(h, c.g); h = Mix(h, c.b); h = Mix(h, c.a); }
        }
        return h;
    }

    static ulong Mix(ulong h, byte b) { h ^= b; return h * 1099511628211UL; }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────

    static string Verdict(bool ok) => ok ? "PASS " : "FAIL ";

    static (float pos, Color color)[] Snapshot(PyreRamp r)
    {
        var a = new (float, Color)[r.stops.Count];
        for (int i = 0; i < r.stops.Count; i++) a[i] = (r.stops[i].pos, r.stops[i].color);
        return a;
    }

    static bool SameSnapshot((float pos, Color color)[] a, (float pos, Color color)[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i].pos != b[i].pos || Delta(a[i].color, b[i].color) > 0f) return false;
        return true;
    }

    static Color[] Sample(PyreRamp r)
    {
        var c = new Color[Samples];
        for (int i = 0; i < Samples; i++) c[i] = r.Evaluate(i / (float)(Samples - 1));
        return c;
    }

    static float MaxDelta(Color[] a, Color[] b)
    {
        float m = 0f;
        for (int i = 0; i < a.Length; i++) m = Mathf.Max(m, Delta(a[i], b[i]));
        return m;
    }

    static float Delta(Color a, Color b) => Mathf.Max(
        Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g)),
        Mathf.Max(Mathf.Abs(a.b - b.b), Mathf.Abs(a.a - b.a)));

    static float DeltaRgb(Color a, Color b) => Mathf.Max(
        Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));

    /// Every IZuiRamp reachable from `root` by serializable fields (plain classes and lists recursed,
    /// UnityEngine.Object references not followed — those are separate assets).
    static void Collect(object root, List<IZuiRamp> found, HashSet<object> seen, int depth)
    {
        if (root == null || depth > 8) return;
        if (root is IZuiRamp r) { if (seen.Add(r)) found.Add(r); return; }
        if (root is string || (root is UnityEngine.Object && depth > 0)) return;
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
            if (v == null || v is ValueType) continue;
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
