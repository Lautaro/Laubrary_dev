using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Shaper;

public static class Tests2
{
    // ── The consequence of the missed slot: HS-6.6's Depth reports air as solid ──────────────────
    public static void DepthCorruption()
    {
        Console.WriteLine("=== DP — what the missed crossing pair does to HS-6.6's Depth ===");
        foreach (float slot in new float[] { 60f, 40f, 20f, 8f })
        {
            var root = March2.SlottedPlate(200f, 200f, slot * 0.5f, 40f);
            var prog = ShaperCompiler.Compile(root, 0f, 0u);
            var stack = prog.NewStack();
            var def = new ShaperHeightDef { technique = ShaperExtrusionTechnique.Flat, bevel = ShaperBevelTechnique.None, depth = new ZUIValue(40f) };
            var op = ShaperHeightCompiler.Compile(def, prog, 1f, 0f, 0f, 0u);
            var scene = new ShaperResolveScene(); scene.Add(prog, op);
            var buf = new ShaperCrossing[64];
            float ox = -400f, oy = 0f, oz = op.baseZ + 20f;
            var r = ShaperResolve.Query(scene, ox, oy, oz, 1f, 0f, 0f, buf);
            float reported = ShaperResolve.Depth(buf, r.count, 0);
            // truth: total solid length along the ray = 400 - slot
            float truth = 400f - slot;
            Console.WriteLine(string.Format("  slot={0,4}  crossings reported={1}  Depth reported={2,8:F3}  true solid={3,8:F3}  error={4,8:F3} canvas px",
                slot, r.count, reported, truth, reported - truth));
        }
    }

    // ── Truncation parity: implementer's own open item 4 ─────────────────────────────────────────
    public static void Truncation()
    {
        Console.WriteLine();
        Console.WriteLine("=== TR — parity stamped on a TRUNCATED crossing slice (implementer's open item 4) ===");
        // A stepped profile on a tilted ray gives many crossings; truncate the buffer hard.
        var root = March.HollowPlate(200f, 200f, 45f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var def = new ShaperHeightDef { technique = ShaperExtrusionTechnique.Dome, bevel = ShaperBevelTechnique.Cove, depth = new ZUIValue(300f), bevelAmount = new ZUIValue(0.4f) };
        var op = ShaperHeightCompiler.Compile(def, prog, 1f, 0f, 0f, 0u);
        var scene = new ShaperResolveScene(); scene.Add(prog, op);

        var full = new ShaperCrossing[256];
        float ox = -300f, oy = 0f, oz = op.baseZ + 120f;
        float dx = 1f, dy = 0.0f, dz = -0.28f;
        var rf = ShaperResolve.Query(scene, ox, oy, oz, dx, dy, dz, full);
        var st = prog.NewStack();
        var tr = March.TrueCrossings(prog, st, op, ox, oy, oz, dx/(float)Math.Sqrt(dx*dx+dy*dy+dz*dz), dy/(float)Math.Sqrt(dx*dx+dy*dy+dz*dz), dz/(float)Math.Sqrt(dx*dx+dy*dy+dz*dz), 1500f, 1500000);
        Console.WriteLine("  GROUND TRUTH crossings on this ray: " + tr.Count);
        Console.WriteLine("  untruncated: count=" + rf.count + " truncated=" + rf.truncated + "  branch=" + rf.branch);
        Console.Write("    parity full : ");
        for (int i = 0; i < Math.Min(rf.count, 12); i++) Console.Write(full[i].entering ? "I" : "O");
        Console.WriteLine();
        for (int cap = 1; cap <= Math.Min(6, rf.count); cap++)
        {
            var small = new ShaperCrossing[cap];
            var rs = ShaperResolve.Query(scene, ox, oy, oz, dx, dy, dz, small);
            bool sameOrder = true, sameParity = true;
            for (int i = 0; i < rs.count; i++)
            {
                if (i < rf.count && Math.Abs(small[i].rayT - full[i].rayT) > 1e-3f) sameOrder = false;
                if (i < rf.count && small[i].entering != full[i].entering) sameParity = false;
            }
            Console.Write(string.Format("    cap={0} count={1} truncated={2}  order matches full: {3}  parity matches full: {4}   parity: ", cap, rs.count, rs.truncated, sameOrder, sameParity));
            for (int i = 0; i < rs.count; i++) Console.Write(small[i].entering ? "I" : "O");
            Console.WriteLine();
        }
    }

    // ── HS-7.2 Z offset ──────────────────────────────────────────────────────────────────────────
    public static void ZOffset()
    {
        Console.WriteLine();
        Console.WriteLine("=== Z — HS-7.2 base(i) = i*layerSpacing + zOffset(i) ===");
        var doc = new ShaperDocument();
        doc.layerSpacing = 0.75f;
        for (int i = 0; i < 4; i++)
        {
            var L = new ShaperLayer();
            L.zOffset = new ZUIValue(i == 2 ? -50f : (i == 3 ? 12.5f : 0f));
            doc.layers.Add(L);
        }
        for (int i = 0; i < 4; i++)
        {
            float b = ShaperHeightCompiler.LayerBase(doc, i, 0f, 0u);
            float want = i * 0.75f + (i == 2 ? -50f : (i == 3 ? 12.5f : 0f));
            Console.WriteLine(string.Format("  layer {0}: base={1,10:F4}  expected={2,10:F4}  {3}", i, b, want, Math.Abs(b - want) < 1e-5f ? "ok" : "*** MISMATCH"));
        }
        Console.WriteLine("  default layerSpacing = " + new ShaperDocument().layerSpacing + " (HS-7.1 says 0.75)");
        // the -9999 sentinel trap HS-7.3 says is NOT inherited
        Console.WriteLine("  a large negative zOffset is not clamped or sentinel-tested: base(0) with zOffset -100000 = " + LayerBaseWith(-100000f));
    }
    static float LayerBaseWith(float z)
    {
        var doc = new ShaperDocument(); doc.layerSpacing = 0.75f;
        var L = new ShaperLayer(); L.zOffset = new ZUIValue(z); doc.layers.Add(L);
        return ShaperHeightCompiler.LayerBase(doc, 0, 0f, 0u);
    }

    // ── INJECTION: does H9's gate actually detect a perturbed G'? ────────────────────────────────
    public static void InjectH9()
    {
        Console.WriteLine();
        Console.WriteLine("=== INJ-H9 — inject a known error into G' and check H9's own gate reads it back ===");
        Console.WriteLine("  gate (H9's, verbatim): err <= max(2e-3 absolute, 5% relative), per point.");
        foreach (double factor in new double[] { 1.0, 1.02, 1.06, 1.20, 2.0 })
        {
            int trips = 0, checks = 0;
            foreach (var tech in Fixtures.Techs)
                foreach (var bev in Fixtures.Bevels)
                {
                    var op = Fixtures.Make(tech, bev, 4f, 1f, 1f, 0.3f, 3f, 1f, 45f);
                    for (int i = 1; i < 400; i++)
                    {
                        double t = i / 400.0;
                        double hh = 1e-6;
                        double cd = (Ref.G(op, t + hh, 1.0) - Ref.G(op, t - hh, 1.0)) / (2 * hh);
                        float an = ShaperHeight.ComposedDerivative(op, (float)t, 0f, 0f);
                        if (float.IsPositiveInfinity(an) || Math.Abs(cd) > 1e4) continue;
                        double perturbed = an * factor;
                        checks++;
                        if (Math.Abs(perturbed - cd) > Math.Max(2e-3, 0.05 * Math.Abs(cd))) trips++;
                    }
                }
            Console.WriteLine(string.Format("  G' scaled by {0,5:F2}x -> gate trips on {1,6} of {2} points ({3:F2}%)", factor, trips, checks, 100.0 * trips / checks));
        }
    }

    // ── INJECTION: does H4's zeta grid reach the region where the closed forms fail? ─────────────
    public static void InjectH4()
    {
        Console.WriteLine();
        Console.WriteLine("=== INJ-H4 — H4's zeta grid is i/200*supG. What does it miss? ===");
        foreach (var bev in new[] { ShaperBevelTechnique.Rounded, ShaperBevelTechnique.Cove, ShaperBevelTechnique.Ogee })
            foreach (float am in new float[] { 0.05f, 0.25f, 1f })
            {
                var op = Fixtures.Make(ShaperExtrusionTechnique.Flat, bev, 4f, 1f, 1f, am, 3f, 1f, 45f);
                double h4worst = 0, myworst = 0; float h4at = 0, myat = 0;
                for (int i = 1; i <= 200; i++)
                {
                    float z = (i / 200f) * op.supG;
                    float inv = ShaperHeight.Inverse(op, z, 0f, 0f);
                    if (float.IsPositiveInfinity(inv)) continue;
                    double s = z - ShaperHeight.Composed(op, inv, 0f, 0f);
                    if (s > h4worst) { h4worst = s; h4at = z; }
                }
                for (int e = 0; e <= 60; e++)
                {
                    float z = (float)(Math.Pow(10.0, -e * 0.15) * op.supG);
                    if (z <= 0) continue;
                    float inv = ShaperHeight.Inverse(op, z, 0f, 0f);
                    if (float.IsPositiveInfinity(inv)) continue;
                    double s = z - ShaperHeight.Composed(op, inv, 0f, 0f);
                    if (s > myworst) { myworst = s; myat = z; }
                }
                Console.WriteLine(string.Format("  Flat+{0,-8} amount={1,-6} H4 grid worst reach shortfall={2:E3} (at zeta={3:E2});  log grid worst={4:E3} (at zeta={5:E2})   ratio={6:F0}x",
                    bev, am, h4worst, h4at, myworst, myat, myworst / Math.Max(1e-12, h4worst)));
            }
    }

    // ── The general branch on a MULTI-LAYER scene, and sorting ───────────────────────────────────
    public static void MultiLayer()
    {
        Console.WriteLine();
        Console.WriteLine("=== ML — multi-layer scene: is the merged crossing list sorted and correctly paired? ===");
        var root = March.SolidPlate(120f, 120f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var scene = new ShaperResolveScene();
        for (int i = 0; i < 3; i++)
        {
            var def = new ShaperHeightDef { technique = ShaperExtrusionTechnique.Flat, bevel = ShaperBevelTechnique.None, depth = new ZUIValue(20f) };
            var op = ShaperHeightCompiler.Compile(def, prog, 1f, i * 60f, 0f, 0u);
            scene.Add(prog, op);
        }
        var buf = new ShaperCrossing[32];
        var r = ShaperResolve.Query(scene, 5f, 5f, 400f, 0.2f, 0.1f, -1f, buf);
        bool sorted = true;
        for (int i = 1; i < r.count; i++) if (buf[i].rayT < buf[i - 1].rayT) sorted = false;
        Console.WriteLine("  crossings=" + r.count + " (expect 6)  sortedByRayT=" + sorted + "  branch=" + r.branch);
        for (int i = 0; i < r.count; i++)
            Console.WriteLine(string.Format("    layer={0} rayT={1,9:F3} entering={2,-5} kind={3,-4} height={4,8:F3} edgeDistance={5,8:F3}", buf[i].layer, buf[i].rayT, buf[i].entering, buf[i].kind, buf[i].height, buf[i].edgeDistance));
    }
}
