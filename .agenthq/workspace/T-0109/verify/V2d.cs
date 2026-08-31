using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.Shaper;

// V2C-2 — a BROAD sweep for the truncation-parity hazard: many rays x many buffer caps,
// each retained crossing's stamped `entering` compared against its TRUE parity from an
// independent scan, and Depth compared against the true solid length.
public static class V2Trunc
{
    public static void Run()
    {
        Console.WriteLine("=== V2C-2 - truncation parity, broad sweep ===");
        Console.WriteLine("For every ray x every buffer cap: does the stamped `entering` match the crossing's TRUE parity,");
        Console.WriteLine("and does Depth ever OVER-report the true solid length (the answer HS-6.6 forbids by name)?");
        Console.WriteLine();

        var root = V2.MultiSlotPlate(200f, 40f);
        var prog = ShaperCompiler.Compile(root, 0f, 0u);
        var stack = prog.NewStack();

        int totalCases = 0, parityBad = 0, overRep = 0, outOfOrder = 0;
        double worstOver = 0; string worstAt = "";
        int worstParity = 0; string worstPAt = "";

        foreach (int steps in new int[] { 4, 8, 16 })
        foreach (float tilt in new float[] { 20f, 30f, 40f, 50f, 60f, 70f, 80f })
        foreach (float z0 in new float[] { 40f, 80f, 120f, 180f, 240f, 290f })
        {
            var def = new ShaperHeightDef {
                technique = ShaperExtrusionTechnique.Stepped,
                steps = new ZUIValue(steps),
                bevel = ShaperBevelTechnique.None,
                depth = new ZUIValue(300f) };
            var op = ShaperHeightCompiler.Compile(def, prog, 1f, 0f, 0f, 0u);
            double rad = tilt * Math.PI / 180.0;
            float dx = (float)Math.Cos(rad), dz = (float)(-Math.Sin(rad));
            float ox = -196f;
            var scene = new ShaperResolveScene(); scene.Add(prog, op);
            var full = new ShaperCrossing[256];
            var rF = ShaperResolve.Query(scene, ox, 0f, z0, dx, 0f, dz, full);
            if (rF.count < 4) continue;
            var truth = March.TrueCrossings(prog, stack, op, ox, 0f, z0, dx, 0f, dz, 900f, 600000);
            if (truth.Count != rF.count) continue;             // only judge rays the march gets right untruncated
            double trueDepth = 0;
            for (int i = 0; i + 1 < truth.Count; i += 2) trueDepth += truth[i + 1] - truth[i];

            for (int cap = 1; cap < rF.count; cap++)
            {
                var buf = new ShaperCrossing[cap];
                var r = ShaperResolve.Query(scene, ox, 0f, z0, dx, 0f, dz, buf);
                totalCases++;
                // is the retained set the FIRST `cap` in ray order?
                bool prefix = true;
                for (int i = 0; i < r.count; i++) if (Math.Abs(buf[i].rayT - truth[i]) > 0.05) { prefix = false; break; }
                if (!prefix) outOfOrder++;
                // true parity: truth[j] alternates starting entering=true at j=0
                int bad = 0;
                for (int i = 0; i < r.count; i++)
                {
                    int j = -1; double best = 1e30;
                    for (int k = 0; k < truth.Count; k++) { double e = Math.Abs(truth[k] - buf[i].rayT); if (e < best) { best = e; j = k; } }
                    bool trueEnt = (j % 2) == 0;
                    if (buf[i].entering != trueEnt) bad++;
                }
                if (bad > 0) { parityBad++; if (bad > worstParity) { worstParity = bad; worstPAt = "steps=" + steps + " tilt=" + tilt + " z0=" + z0 + " cap=" + cap; } }
                float depth = ShaperResolve.Depth(buf, r.count, 0);
                if (depth > trueDepth + 0.05)
                {
                    overRep++;
                    if (depth - trueDepth > worstOver) { worstOver = depth - trueDepth; worstAt = "steps=" + steps + " tilt=" + tilt + " z0=" + z0 + " cap=" + cap + "  Depth=" + depth.ToString("F3") + " true=" + trueDepth.ToString("F3"); }
                }
            }
        }
        Console.WriteLine("  truncated cases exercised            : " + totalCases);
        Console.WriteLine("  cases whose retained set is NOT a ray-order prefix (i.e. emission WAS out of order): " + outOfOrder);
        Console.WriteLine("  cases with at least one MIS-STAMPED parity flag: " + parityBad + "   worst flags wrong in one case: " + worstParity);
        if (worstPAt != "") Console.WriteLine("      worst parity case: " + worstPAt);
        Console.WriteLine("  cases whose Depth OVER-reports true solid: " + overRep + "   worst over-report: " + worstOver.ToString("F3"));
        if (worstAt != "") Console.WriteLine("      worst: " + worstAt);
    }
}
