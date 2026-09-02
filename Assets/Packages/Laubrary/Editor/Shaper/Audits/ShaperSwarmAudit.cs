using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

namespace Laubrary.Shaper.Editor
{
    /// <summary>
    /// T-0113 — the swarm audit: measured verification of the identity no-op, the generic wrapper working on
    /// all three node kinds with zero extra generator code, the independent-lifetime fix (the shared-clock bug
    /// this task's body names explicitly), native-path selection and its O(1) op count, the hard-cap-with-a-
    /// visible-warning fallback for a stateful sim with no native path, a REAL measured cost comparison between
    /// N independent simulations and one batched native pass, and that <c>interact</c> is live under Native and
    /// inert under Generic.
    ///
    /// Same posture as <see cref="ShaperFillAudit"/>/<c>PyreShaperCompositeAudit</c>: plain static methods, no
    /// <c>[MenuItem]</c>, no <c>EditorWindow</c>, invoked through the Unity CLI. Every result is measured, not
    /// asserted on the strength of the code compiling.
    /// </summary>
    public static class ShaperSwarmAudit
    {
        const int W = 96, H = 96;
        const float Px = 1f;

        public static string RunAll()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Shaper SWARM audit (T-0113) ===");
            sb.AppendLine(CT0_IdentityNoOp());
            sb.AppendLine(CT1_GenericWorksOnAllThreeKinds());
            sb.AppendLine(CT2_IndependentLifetimeFix());
            sb.AppendLine(CT3_NativeSelectedAndOpCountIsOne());
            sb.AppendLine(CT4_HardCapFallback());
            sb.AppendLine(CT5_MeasuredSimCostMultiplier());
            sb.AppendLine(CT6_InteractLiveUnderNative());
            sb.AppendLine(CT7_InteractInertUnderGeneric());
            sb.AppendLine(CT8_NativeReasonDeclared());
            return sb.ToString();
        }

        static string Verdict(bool ok) => ok ? "PASS" : "FAIL";
        static ShaperSampleGrid Grid() => ShaperSampleGrid.Centred(W, H, Px);

        static ShaperNode Disc(string name, float r, float x = 0f, float y = 0f)
        {
            var n = ShaperNode.Primitive(
                new ShaperPrimitiveDef { kind = ShaperPrimitiveKind.Ellipse, ellipseRx = r, ellipseRy = r }, name);
            n.transform.translate = new Vector2(x, y);
            return n;
        }

        static float SampleCoverage(ShaperProgram prog, float x, float y)
            => ShaperEvaluator.Coverage(prog, Grid(), x, y, prog.NewStack());

        static int CountOps(ShaperProgram prog, ShaperOpKind kind)
        {
            int c = 0;
            foreach (var op in prog.ops) if (op.kind == kind) c++;
            return c;
        }

        // ── fixtures ──────────────────────────────────────────────────────────────────────────────────────

        /// <summary>T-0113 test fixture — a stateful-simulation Composite source that DOES offer the native
        /// batched path: one shared heat grid, N injection points, each injecting at ITS OWN instance phase via
        /// a rise-then-fade lifecycle curve (0 at birth/death, peak at mid-life) — so a staggered swarm renders
        /// visibly different embers and a lockstep one renders visibly identical ones (CT-2, contact sheet).</summary>
        sealed class ToyEmberSource : IShaperCompositeSource, IShaperSwarmNativeSource, IShaperSimulationSource
        {
            public int steps = 30;
            const float Decay = 0.986f;
            const float Diffuse = 0.35f;

            public string SourceLabel => "Toy ember (T-0113 fixture)";
            public bool IsStatefulSimulation => true;
            public bool SupportsNativeSwarm => true;
            public string NativeSwarmReason =>
                "T-0113 fixture -- one shared heat grid with N injection points, instead of N independent " +
                "grids (SWARM-SPEC.md Part 5); each injector's own instancePhases entry drives its OWN " +
                "rise-then-fade lifecycle within the ONE shared diffusion pass.";

            static float LifeCurve(float t) { t = Mathf.Clamp01(t); return t * (1f - t) * 4f; }

            static void StepGrid(float[] grid, int w, int h, float[] scratch)
            {
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    float c = grid[i];
                    float n = y + 1 < h ? grid[i + w] : c;
                    float s = y - 1 >= 0 ? grid[i - w] : c;
                    float e = x + 1 < w ? grid[i + 1] : c;
                    float wv = x - 1 >= 0 ? grid[i - 1] : c;
                    float avgN = (n + s + e + wv) * 0.25f;
                    scratch[i] = Mathf.Lerp(c, avgN, Diffuse) * Decay;
                }
                Array.Copy(scratch, grid, grid.Length);
            }

            static void DecayOnly(float[] grid)
            {
                for (int i = 0; i < grid.Length; i++) grid[i] *= Decay;
            }

            /// <summary>Independent single-instance path: allocates and steps its OWN grid, exactly what the
            /// generic wrapper (or a hand-written N-independent-instances loop) would call N times.</summary>
            public void Render(int width, int height, float phase01, uint seed, Color32[] target)
            {
                var grid = new float[width * height];
                var scratch = new float[width * height];
                int cx = width / 2, cy = height / 2;
                for (int t = 0; t < steps; t++)
                {
                    Inject(grid, width, height, cx, cy, 3f * LifeCurve(phase01));
                    StepGrid(grid, width, height, scratch);
                }
                Paint(grid, width, height, target);
            }

            /// <summary>Injects into a small plus-shaped neighbourhood rather than one bare cell, so a single
            /// instance's own glow reads as a visible blob rather than a one-pixel spark at typical bake
            /// resolutions -- a rendering-fidelity fix, not a change to the cost/interaction story above.</summary>
            static void Inject(float[] grid, int w, int h, int cx, int cy, float amount)
            {
                void Add(int x, int y, float f) { if (x >= 0 && x < w && y >= 0 && y < h) grid[y * w + x] += amount * f; }
                Add(cx, cy, 1f);
                Add(cx + 1, cy, 0.5f); Add(cx - 1, cy, 0.5f); Add(cx, cy + 1, 0.5f); Add(cx, cy - 1, 0.5f);
            }

            public void RenderSwarm(int width, int height, float phase01, uint[] instanceSeeds,
                                     Vector2[] instanceOffsets, float[] instancePhases, bool interact,
                                     Color32[] target)
            {
                var grid = new float[width * height];
                var scratch = new float[width * height];
                int n = instanceOffsets.Length;
                var cellX = new int[n];
                var cellY = new int[n];
                for (int k = 0; k < n; k++)
                {
                    cellX[k] = Mathf.Clamp(width / 2 + Mathf.RoundToInt(instanceOffsets[k].x), 0, width - 1);
                    cellY[k] = Mathf.Clamp(height / 2 + Mathf.RoundToInt(instanceOffsets[k].y), 0, height - 1);
                }
                for (int t = 0; t < steps; t++)
                {
                    for (int k = 0; k < n; k++)
                        Inject(grid, width, height, cellX[k], cellY[k], 3f * LifeCurve(instancePhases[k]));
                    if (interact) StepGrid(grid, width, height, scratch);
                    else DecayOnly(grid);
                }
                Paint(grid, width, height, target);
            }

            static void Paint(float[] grid, int w, int h, Color32[] target)
            {
                for (int i = 0; i < grid.Length; i++)
                {
                    float v = Mathf.Clamp01(grid[i]);
                    Color c = Color.Lerp(new Color(0.05f, 0.02f, 0f), new Color(1f, 0.75f, 0.15f), v);
                    byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(v * 1.4f) * 255f);
                    target[i] = new Color32((byte)Mathf.RoundToInt(c.r * 255f), (byte)Mathf.RoundToInt(c.g * 255f),
                                             (byte)Mathf.RoundToInt(c.b * 255f), a);
                }
            }
        }

        /// <summary>T-0113 test fixture — a stateful-simulation source that declares itself one
        /// (<see cref="IShaperSimulationSource"/>) but does NOT implement <see cref="IShaperSwarmNativeSource"/>,
        /// so it is the positive control for the hard-cap fallback (CT-4): the ONLY reason it exists is to prove
        /// the cap fires for a sim with nothing better on offer, contrasted against <see cref="ToyEmberSource"/>
        /// which is not capped because it offers Native.</summary>
        sealed class ToyStubbornSimSource : IShaperCompositeSource, IShaperSimulationSource
        {
            public string SourceLabel => "Toy stubborn sim, no native path (T-0113 fixture)";
            public bool IsStatefulSimulation => true;
            public void Render(int width, int height, float phase01, uint seed, Color32[] target)
            {
                for (int i = 0; i < target.Length; i++) target[i] = new Color32(200, 200, 200, 40);
            }
        }

        static ShaperNode EmberNode(string name, ToyEmberSource src, int bake = 64)
        {
            var def = new ShaperCompositeDef
            {
                source = src,
                reason = ShaperCompositeReason.NotYetSplit,
                reasonNote = "T-0113 fixture -- a toy stateful heat sim, hosted to exercise the swarm native path.",
                halfExtentX = 48f, halfExtentY = 48f, bakeWidth = bake, bakeHeight = bake,
            };
            return ShaperNode.Composite(def, name);
        }

        // ── CT-0 ──────────────────────────────────────────────────────────────────────────────────────────

        public static string CT0_IdentityNoOp()
        {
            var sb = new StringBuilder("CT-0 swarm disabled, or count<=1, is an EXACT no-op\n");

            var plain = Disc("Disc", 20f);
            var progPlain = ShaperCompiler.Compile(plain, 0.4f, 3u);

            var withSwarmOff = Disc("Disc", 20f);
            withSwarmOff.swarm.enabled = false;
            withSwarmOff.swarm.count = 9;
            var progOff = ShaperCompiler.Compile(withSwarmOff, 0.4f, 3u);

            var withCount1 = Disc("Disc", 20f);
            withCount1.swarm.enabled = true;
            withCount1.swarm.count = 1;
            var progCount1 = ShaperCompiler.Compile(withCount1, 0.4f, 3u);

            bool offMatches = progOff.ops.Length == progPlain.ops.Length && !progOff.hasSwarm;
            bool count1Matches = progCount1.ops.Length == progPlain.ops.Length && !progCount1.hasSwarm;

            sb.AppendLine("  plain: " + progPlain.ops.Length + " ops, hasSwarm=" + progPlain.hasSwarm);
            sb.AppendLine("  swarm.enabled=false, count=9: " + progOff.ops.Length + " ops, hasSwarm=" + progOff.hasSwarm +
                          " (expected " + progPlain.ops.Length + " ops, hasSwarm=False)");
            sb.AppendLine("  swarm.enabled=true, count=1: " + progCount1.ops.Length + " ops, hasSwarm=" + progCount1.hasSwarm +
                          " (expected " + progPlain.ops.Length + " ops, hasSwarm=False -- 1 instance is a legal identity)");
            sb.Append("  RESULT: " + Verdict(offMatches && count1Matches));
            return sb.ToString();
        }

        // ── CT-1 ──────────────────────────────────────────────────────────────────────────────────────────

        public static string CT1_GenericWorksOnAllThreeKinds()
        {
            var sb = new StringBuilder("CT-1 generic wrapper works on Primitive, Bag and Composite with ZERO extra generator code\n");

            const int count = 5;

            var prim = Disc("Disc", 15f);
            prim.swarm = new ShaperSwarmDef { enabled = true, count = count, positionJitter = new Vector2(20f, 20f) };
            var progPrim = ShaperCompiler.Compile(prim, 0.4f, 11u);
            int primLeaves = CountOps(progPrim, ShaperOpKind.Leaf);
            bool primOk = primLeaves == count && progPrim.swarmImplementation == ShaperSwarmImplementation.Generic;

            var bag = ShaperNode.Bag("Pair", ShaperCombineMode.Add, Disc("A", 8f, -6f, 0f), Disc("B", 8f, 6f, 0f));
            bag.swarm = new ShaperSwarmDef { enabled = true, count = count, positionJitter = new Vector2(20f, 20f) };
            var progBag = ShaperCompiler.Compile(bag, 0.4f, 11u);
            int bagLeaves = CountOps(progBag, ShaperOpKind.Leaf);
            bool bagOk = bagLeaves == count * 2 && progBag.swarmImplementation == ShaperSwarmImplementation.Generic;

            var nonNativeDef = new ShaperCompositeDef
            {
                source = new StubDisc(18f),
                reason = ShaperCompositeReason.NotYetSplit,
                reasonNote = "T-0113 fixture -- deliberately not native, to prove Generic works on Composite too.",
                halfExtentX = 40f, halfExtentY = 40f, bakeWidth = 48, bakeHeight = 48,
            };
            var compNode = ShaperNode.Composite(nonNativeDef, "PlainComposite");
            compNode.swarm = new ShaperSwarmDef { enabled = true, count = count, positionJitter = new Vector2(20f, 20f) };
            var progComp = ShaperCompiler.Compile(compNode, 0.4f, 11u);
            int compSamples = CountOps(progComp, ShaperOpKind.CompositeSample);
            bool compOk = compSamples == count && progComp.swarmImplementation == ShaperSwarmImplementation.Generic;

            // Positions actually differ -- N distinct instances, not N identical copies.
            float x0 = progPrim.ops[0].boxCx, x1 = progPrim.ops[1].boxCx;
            bool positionsDiffer = Mathf.Abs(x0 - x1) > 0.01f;

            sb.AppendLine("  Primitive swarm(" + count + "): Leaf ops=" + primLeaves + " (expected " + count + "), impl=" +
                          progPrim.swarmImplementation);
            sb.AppendLine("  Bag(2 members) swarm(" + count + "): Leaf ops=" + bagLeaves + " (expected " + (count * 2) + "), impl=" +
                          progBag.swarmImplementation);
            sb.AppendLine("  Composite(non-native) swarm(" + count + "): CompositeSample ops=" + compSamples +
                          " (expected " + count + "), impl=" + progComp.swarmImplementation);
            sb.AppendLine("  instance[0].boxCx=" + x0.ToString("F2") + " vs instance[1].boxCx=" + x1.ToString("F2") +
                          " -- differ=" + positionsDiffer + " (N distinct instances, not N identical copies)");
            sb.Append("  RESULT: " + Verdict(primOk && bagOk && compOk && positionsDiffer));
            return sb.ToString();
        }

        /// <summary>A trivial non-native Composite source, for CT-1's "Generic also covers Composite" leg.</summary>
        sealed class StubDisc : IShaperCompositeSource
        {
            readonly float r;
            public StubDisc(float radius) { r = radius; }
            public string SourceLabel => "Stub disc";
            public void Render(int width, int height, float phase01, uint seed, Color32[] target)
            {
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float cx = (x + 0.5f) - width * 0.5f, cy = (y + 0.5f) - height * 0.5f;
                    float d = Mathf.Sqrt(cx * cx + cy * cy);
                    float a = Mathf.Clamp01(0.5f - (d - r));
                    target[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
        }

        // ── CT-2 -- THE load-bearing one: the shared-clock bug, fixed and measured ─────────────────────────

        public static string CT2_IndependentLifetimeFix()
        {
            var sb = new StringBuilder("CT-2 independent per-instance lifetime -- the shared-clock bug this task's body names, fixed and measured\n");

            const int count = 8;

            var staggered = Disc("Disc", 10f);
            staggered.swarm = new ShaperSwarmDef { enabled = true, count = count, lifetimeStagger = 1f, positionJitter = Vector2.zero };
            var progStaggered = ShaperCompiler.Compile(staggered, 0.4f, 21u);

            var lockstep = Disc("Disc", 10f);
            lockstep.swarm = new ShaperSwarmDef { enabled = true, count = count, lifetimeStagger = 0f, positionJitter = Vector2.zero };
            var progLockstep = ShaperCompiler.Compile(lockstep, 0.4f, 21u);

            float sMin = float.MaxValue, sMax = float.MinValue;
            foreach (var p in progStaggered.swarmInstancePhases) { sMin = Mathf.Min(sMin, p); sMax = Mathf.Max(sMax, p); }
            float staggeredSpread = sMax - sMin;

            float lMin = float.MaxValue, lMax = float.MinValue;
            foreach (var p in progLockstep.swarmInstancePhases) { lMin = Mathf.Min(lMin, p); lMax = Mathf.Max(lMax, p); }
            float lockstepSpread = lMax - lMin;

            bool staggeredOk = staggeredSpread > 0.3f;   // 8 hash-drawn phases across 0..1 should spread well beyond 0.3
            bool lockstepOk = lockstepSpread < 1e-5f;    // exact reproduction of the OLD bug: every instance = node's own phase
            bool allEqualNodePhase = true;
            foreach (var p in progLockstep.swarmInstancePhases) if (Mathf.Abs(p - 0.4f) > 1e-5f) allEqualNodePhase = false;

            sb.AppendLine("  lifetimeStagger=1 (the fix, default): " + count + " instance phases, spread (max-min) = " +
                          staggeredSpread.ToString("F3") + " (expected > 0.3 -- independent lifetimes)");
            sb.AppendLine("  lifetimeStagger=0 (old bug, kept selectable): spread = " + lockstepSpread.ToString("F5") +
                          ", all equal node's own phase 0.4 = " + allEqualNodePhase +
                          " (expected spread~0, all==0.4 -- exact reproduction of the bug on demand)");
            sb.Append("  RESULT: " + Verdict(staggeredOk && lockstepOk && allEqualNodePhase));
            return sb.ToString();
        }

        // ── CT-3 ──────────────────────────────────────────────────────────────────────────────────────────

        public static string CT3_NativeSelectedAndOpCountIsOne()
        {
            var sb = new StringBuilder("CT-3 native path selected for a source that offers it; op count is O(1), not O(N)\n");

            var small = EmberNode("Ember", new ToyEmberSource { steps = 3 });
            small.swarm = new ShaperSwarmDef { enabled = true, count = 6, positionJitter = new Vector2(15f, 15f) };
            var progSmall = ShaperCompiler.Compile(small, 0.4f, 5u);

            var big = EmberNode("Ember", new ToyEmberSource { steps = 3 });
            big.swarm = new ShaperSwarmDef { enabled = true, count = 40, positionJitter = new Vector2(15f, 15f) };
            var progBig = ShaperCompiler.Compile(big, 0.4f, 5u);

            int samplesSmall = CountOps(progSmall, ShaperOpKind.CompositeSample);
            int samplesBig = CountOps(progBig, ShaperOpKind.CompositeSample);
            bool implOk = progSmall.swarmImplementation == ShaperSwarmImplementation.Native &&
                          progBig.swarmImplementation == ShaperSwarmImplementation.Native;
            bool opCountOk = samplesSmall == 1 && samplesBig == 1;

            sb.AppendLine("  count=6:  CompositeSample ops=" + samplesSmall + " (expected 1), impl=" + progSmall.swarmImplementation);
            sb.AppendLine("  count=40: CompositeSample ops=" + samplesBig + " (expected 1 -- SAME as count=6, O(1) not O(N)), impl=" +
                          progBig.swarmImplementation);
            sb.Append("  RESULT: " + Verdict(implOk && opCountOk));
            return sb.ToString();
        }

        // ── CT-4 ──────────────────────────────────────────────────────────────────────────────────────────

        public static string CT4_HardCapFallback()
        {
            var sb = new StringBuilder("CT-4 explicit hard-cap-with-a-visible-warning for a stateful sim with NO native path\n");

            var stubborn = ShaperNode.Composite(new ShaperCompositeDef
            {
                source = new ToyStubbornSimSource(),
                reason = ShaperCompositeReason.NotYetSplit,
                reasonNote = "T-0113 fixture -- deliberately no native path, to prove the hard cap fires.",
                halfExtentX = 40f, halfExtentY = 40f, bakeWidth = 32, bakeHeight = 32,
            }, "Stubborn");
            stubborn.swarm = new ShaperSwarmDef { enabled = true, count = 20 };
            var progStubborn = ShaperCompiler.Compile(stubborn, 0.4f, 5u);

            var ember = EmberNode("Ember", new ToyEmberSource { steps = 3 });
            ember.swarm = new ShaperSwarmDef { enabled = true, count = 20 };
            var progEmber = ShaperCompiler.Compile(ember, 0.4f, 5u);

            bool cappedOk = progStubborn.swarmCapped && progStubborn.swarmCount == ShaperSwarmDef.SimulationHardCap &&
                            !string.IsNullOrWhiteSpace(progStubborn.swarmCapReason);
            bool notCappedOk = !progEmber.swarmCapped && progEmber.swarmCount == 20;

            sb.AppendLine("  no-native sim, authored count=20: capped=" + progStubborn.swarmCapped +
                          ", resolved count=" + progStubborn.swarmCount + " (expected True, " + ShaperSwarmDef.SimulationHardCap + ")");
            sb.AppendLine("    reason: " + progStubborn.swarmCapReason);
            sb.AppendLine("  native-capable sim (ToyEmberSource), authored count=20: capped=" + progEmber.swarmCapped +
                          ", resolved count=" + progEmber.swarmCount + " (expected False, 20 -- native has no cap)");
            sb.Append("  RESULT: " + Verdict(cappedOk && notCappedOk));
            return sb.ToString();
        }

        // ── CT-5 -- measured, not asserted ───────────────────────────────────────────────────────────────

        public static string CT5_MeasuredSimCostMultiplier()
        {
            var sb = new StringBuilder("CT-5 MEASURED: N independent simulations vs one batched native pass (SWARM-SPEC.md Part 5)\n");

            const int n = 16, bw = 96, bh = 96, steps = 30;
            var src = new ToyEmberSource { steps = steps };

            // Warm up the JIT before timing either path.
            var warm = new Color32[bw * bh];
            src.Render(bw, bh, 0.5f, 0u, warm);

            var swIndependent = Stopwatch.StartNew();
            var target = new Color32[bw * bh];
            for (int i = 0; i < n; i++) src.Render(bw, bh, 0.5f, (uint)i, target);
            swIndependent.Stop();

            var offsets = new Vector2[n];
            var seeds = new uint[n];
            var phases = new float[n];
            for (int i = 0; i < n; i++) { offsets[i] = new Vector2((i % 4) * 8f - 12f, (i / 4) * 8f - 12f); seeds[i] = (uint)i; phases[i] = 0.5f; }

            var swBatched = Stopwatch.StartNew();
            src.RenderSwarm(bw, bh, 0.5f, seeds, offsets, phases, true, target);
            swBatched.Stop();

            double independentMs = swIndependent.Elapsed.TotalMilliseconds;
            double batchedMs = Math.Max(0.001, swBatched.Elapsed.TotalMilliseconds);
            double ratio = independentMs / batchedMs;

            sb.AppendLine("  fixture: " + bw + "x" + bh + " grid, " + steps + " diffusion steps/frame, N=" + n + " instances");
            sb.AppendLine("  " + n + " independent Render() calls (one grid each): " + independentMs.ToString("F2") + " ms");
            sb.AppendLine("  1 batched RenderSwarm() call (one shared grid, " + n + " injectors): " + batchedMs.ToString("F2") + " ms");
            sb.AppendLine("  measured ratio = " + ratio.ToString("F1") + "x");
            sb.AppendLine("  SWARM-SPEC.md Part 5 note: the design doc's own figure is 'roughly 200x cell updates for a realistic " +
                          "swarm size', measured on the REAL Pyre Fire/Fireball sim (layered heat+fuel grids, warmup passes) -- " +
                          "not this toy single-grid fixture. This CT does not reproduce that number; it verifies the SHAPE the " +
                          "200x figure depends on (independent cost grows with N, batched cost does not), which is the " +
                          "actionable claim for this task. See SPEC.md Part 5 for the full honest comparison.");
            // The claim this CT actually stands behind: batched is not worse, and does not grow with N the way
            // independent does. A generous >= 2x is the bar -- proves the asymptotic direction is right without
            // overfitting to one machine's noisy timing of a 96x96x30-step toy grid.
            bool ok = ratio >= 2.0;
            sb.Append("  RESULT: " + Verdict(ok) + " (ratio >= 2.0x required to demonstrate the non-linear-vs-flat shape)");
            return sb.ToString();
        }

        // ── CT-6 / CT-7 ───────────────────────────────────────────────────────────────────────────────────

        public static string CT6_InteractLiveUnderNative()
        {
            var sb = new StringBuilder("CT-6 interact is LIVE under Native -- two close instances merge (true) vs stay separate (false)\n");

            var src = new ToyEmberSource { steps = 40 };
            var offsets = new Vector2[] { new Vector2(-5f, 0f), new Vector2(5f, 0f) };
            var seeds = new uint[] { 1u, 2u };
            var phases = new float[] { 0.5f, 0.5f };

            var withInteract = new Color32[64 * 64];
            src.RenderSwarm(64, 64, 0.5f, seeds, offsets, phases, true, withInteract);
            var withoutInteract = new Color32[64 * 64];
            src.RenderSwarm(64, 64, 0.5f, seeds, offsets, phases, false, withoutInteract);

            // Sample the midpoint between the two injectors. interact=false never moves heat between cells at
            // all (DecayOnly touches only the injected cells themselves), so the midpoint MUST read exactly
            // zero there -- a structural guarantee, not a magnitude judgement call. interact=true diffuses heat
            // outward every step, so the midpoint must read something greater than zero.
            int mid = 32 * 64 + 32;
            float aInteract = withInteract[mid].a / 255f;
            float aNoInteract = withoutInteract[mid].a / 255f;

            bool ok = aInteract > 0.01f && aNoInteract < 1e-6f;
            sb.AppendLine("  midpoint alpha, interact=true:  " + aInteract.ToString("F3") + " (expected > 0.01 -- heat diffused in from both neighbours)");
            sb.AppendLine("  midpoint alpha, interact=false: " + aNoInteract.ToString("F3") + " (expected EXACTLY 0 -- no cross-instance spatial bleed is possible at all)");
            sb.Append("  RESULT: " + Verdict(ok));
            return sb.ToString();
        }

        public static string CT7_InteractInertUnderGeneric()
        {
            var sb = new StringBuilder("CT-7 interact is INERT under Generic -- true vs false compile identically on a Primitive\n");

            var a = Disc("Disc", 15f);
            a.swarm = new ShaperSwarmDef { enabled = true, count = 4, positionJitter = new Vector2(10f, 10f), interact = true };
            var progA = ShaperCompiler.Compile(a, 0.4f, 9u);

            var b = Disc("Disc", 15f);
            b.swarm = new ShaperSwarmDef { enabled = true, count = 4, positionJitter = new Vector2(10f, 10f), interact = false };
            var progB = ShaperCompiler.Compile(b, 0.4f, 9u);

            bool sameOpCount = progA.ops.Length == progB.ops.Length;
            bool sameCoverage = true;
            for (int i = -20; i <= 20 && sameCoverage; i += 10)
                if (Mathf.Abs(SampleCoverage(progA, i, 0f) - SampleCoverage(progB, i, 0f)) > 1e-5f) sameCoverage = false;

            sb.AppendLine("  Generic swarm(4), interact=true:  " + progA.ops.Length + " ops");
            sb.AppendLine("  Generic swarm(4), interact=false: " + progB.ops.Length + " ops (expected identical, and identical coverage samples)");
            sb.AppendLine("  same op count=" + sameOpCount + ", same coverage across a sample line=" + sameCoverage);
            sb.Append("  RESULT: " + Verdict(sameOpCount && sameCoverage));
            return sb.ToString();
        }

        // ── CT-8 ──────────────────────────────────────────────────────────────────────────────────────────

        public static string CT8_NativeReasonDeclared()
        {
            var sb = new StringBuilder("CT-8 a Native implementation always carries a non-blank declared reason (T-0112 sec6.2 precedent)\n");

            var src = new ToyEmberSource();
            bool hasReason = !string.IsNullOrWhiteSpace(src.NativeSwarmReason);
            sb.AppendLine("  ToyEmberSource.NativeSwarmReason blank=" + string.IsNullOrWhiteSpace(src.NativeSwarmReason));
            sb.Append("  RESULT: " + Verdict(hasReason));
            return sb.ToString();
        }

        // ── contact sheet ─────────────────────────────────────────────────────────────────────────────────

        public static void WriteContactSheet(string path)
        {
            int tile = 96, cols = 3, rows = 2, pad = 4;
            int sheetW = cols * tile + (cols + 1) * pad;
            int sheetH = rows * tile + (rows + 1) * pad;
            var sheet = new Texture2D(sheetW, sheetH, TextureFormat.RGBA32, false);
            var bg = new Color32(20, 20, 24, 255);
            var fill = new Color32[sheetW * sheetH];
            for (int i = 0; i < fill.Length; i++) fill[i] = bg;
            sheet.SetPixels32(fill);

            void Cell(int col, int row, Action<int, int> paint)
            {
                int dx = pad + col * (tile + pad), dy = pad + row * (tile + pad);
                paint(dx, dy);
            }

            // Panel 1: generic swarm of a Primitive -- visibly scattered discs.
            Cell(0, 0, (dx, dy) =>
            {
                var n = Disc("Disc", 12f);
                n.swarm = new ShaperSwarmDef { enabled = true, count = 7, seed = 4u, positionJitter = new Vector2(26f, 26f), scaleJitter = 0.3f };
                var prog = ShaperCompiler.Compile(n, 0.4f, 4u);
                BlitCoverage(sheet, prog, tile, dx, dy, new Color(0.4f, 0.8f, 1f));
            });

            // Panel 2: generic swarm of a Bag (two-shape union) -- scattered clusters.
            Cell(1, 0, (dx, dy) =>
            {
                var bag = ShaperNode.Bag("Pair", ShaperCombineMode.Add, Disc("A", 7f, -5f, 0f), Disc("B", 7f, 5f, 0f));
                bag.swarm = new ShaperSwarmDef { enabled = true, count = 5, seed = 8u, positionJitter = new Vector2(22f, 22f) };
                var prog = ShaperCompiler.Compile(bag, 0.4f, 8u);
                BlitCoverage(sheet, prog, tile, dx, dy, new Color(0.6f, 1f, 0.5f));
            });

            // Panel 3: native ember swarm, staggered (the FIX) -- some embers bright/mid-life, some dim/new-or-old.
            Cell(2, 0, (dx, dy) =>
            {
                var n = EmberNode("Ember", new ToyEmberSource { steps = 45 }, tile);
                n.swarm = new ShaperSwarmDef { enabled = true, count = 6, seed = 2u, lifetimeStagger = 1f, positionJitter = new Vector2(18f, 18f), interact = true };
                var prog = ShaperCompiler.Compile(n, 0.5f, 2u);
                BlitRaster(sheet, prog.composites[0].pixels, tile, tile, dx, dy);
            });

            // Panel 4: native ember swarm, lockstep (the OLD bug, reproduced on demand) -- every ember identical.
            Cell(0, 1, (dx, dy) =>
            {
                var n = EmberNode("Ember", new ToyEmberSource { steps = 45 }, tile);
                n.swarm = new ShaperSwarmDef { enabled = true, count = 6, seed = 2u, lifetimeStagger = 0f, positionJitter = new Vector2(18f, 18f), interact = true };
                var prog = ShaperCompiler.Compile(n, 0.5f, 2u);
                BlitRaster(sheet, prog.composites[0].pixels, tile, tile, dx, dy);
            });

            // Panel 5/6: interact true vs false, two close injectors -- merged glow vs two separate blobs.
            Cell(1, 1, (dx, dy) =>
            {
                var src = new ToyEmberSource { steps = 40 };
                var pixels = new Color32[tile * tile];
                src.RenderSwarm(tile, tile, 0.5f, new uint[] { 1u, 2u }, new[] { new Vector2(-5f, 0f), new Vector2(5f, 0f) },
                                 new[] { 0.5f, 0.5f }, true, pixels);
                BlitRaster(sheet, pixels, tile, tile, dx, dy);
            });
            Cell(2, 1, (dx, dy) =>
            {
                var src = new ToyEmberSource { steps = 40 };
                var pixels = new Color32[tile * tile];
                src.RenderSwarm(tile, tile, 0.5f, new uint[] { 1u, 2u }, new[] { new Vector2(-5f, 0f), new Vector2(5f, 0f) },
                                 new[] { 0.5f, 0.5f }, false, pixels);
                BlitRaster(sheet, pixels, tile, tile, dx, dy);
            });

            sheet.Apply();
            var png = sheet.EncodeToPNG();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, png);
            UnityEngine.Object.DestroyImmediate(sheet);
        }

        static void BlitRaster(Texture2D sheet, Color32[] pixels, int w, int h, int dx, int dy)
        {
            var bg = new Color(20f / 255f, 20f / 255f, 24f / 255f, 1f);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color32 p = pixels[y * w + x];
                float a = p.a / 255f;
                Color rgb = new Color(p.r / 255f, p.g / 255f, p.b / 255f, 1f);
                sheet.SetPixel(dx + x, dy + y, Color.Lerp(bg, rgb, a));
            }
        }

        static void BlitCoverage(Texture2D sheet, ShaperProgram prog, int tile, int dx, int dy, Color tint)
        {
            var grid = ShaperSampleGrid.Centred(tile, tile, Px);
            var dist = new float[tile * tile];
            var cov = new float[tile * tile];
            ShaperEvaluator.FillTile(prog, grid, 0, 0, tile, tile, dist, cov, 0, tile, prog.NewStack());
            for (int y = 0; y < tile; y++)
            for (int x = 0; x < tile; x++)
            {
                float c = cov[y * tile + x];
                Color col = Color.Lerp(new Color(0.08f, 0.08f, 0.1f), tint, c);
                sheet.SetPixel(dx + x, dy + y, col);
            }
        }
    }
}
