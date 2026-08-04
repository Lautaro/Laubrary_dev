using System;
using System.Collections.Generic;
using UnityEngine;
using Laubrary.SpriteFx;

namespace Laubrary.Pyre
{
    /// A fundamentally different kind of modifier: genuinely ITERATIVE, unlike every other modifier in Pyre
    /// (which recomputes each frame purely from that frame's own progress, with zero cross-frame state). This
    /// one keeps real state — whatever a concrete subclass needs (a density/velocity grid, spawned emitter
    /// lists, ...) — that carries from frame N to frame N+1, closely modelling a genuine simulation instead of
    /// approximating one in closed form (the "third try" — two prior closed-form attempts, PiercingModifier and
    /// BallisticShockwaveModifier, didn't read as convincing).
    ///
    /// To keep this from disturbing anything else in Pyre: it lives in its OWN single slot
    /// (Pyre.simulationModifier), not the existing Geometry/Pixel/Post modifier LISTS — which trivially
    /// guarantees "only one" (it's a single nullable field, not a list) and "always applied last" (BlastRenderer
    /// calls it exactly once, at the very end of RenderFrame, after every other layer/global modifier has
    /// already fully composited) by construction. Every other modifier, every other asset, is completely
    /// unaffected whether this slot is empty or occupied.
    ///
    /// Correctness across NON-sequential access (the editor preview can scrub/jump to any frame; animated
    /// browser thumbnails sample by wall-clock time and can skip frames or wrap) is handled by EnsureFrame
    /// below: advancing exactly one Step() is only assumed for the frame RIGHT AFTER the one this instance's
    /// state currently reflects; anything else (a jump, OR the same frame re-requested) does a full,
    /// deterministic replay from frame 0, re-resolving each replayed frame's OWN parameters via
    /// `paramsForFrame` (not just whatever the caller most recently resolved for the frame it actually asked
    /// for — see EnsureFrame's own doc). The real bake (BlastRenderer.RenderSheet, the only code path that
    /// produces the actual shipped sprite sheet) already calls RenderFrame in strict ascending order with
    /// nothing else interleaved, so it always hits the cheap O(1)-per-frame path; only interactive scrubbing
    /// pays the O(frame) replay cost.
    ///
    /// An earlier version of this class also had a cheap "same frame re-request" path (checkpoint-restore
    /// instead of a full replay), reasoned as "for a slider dragged while the preview is paused". That was
    /// wrong: dragging a slider changes this modifier's OWN parameters, and a checkpoint only undoes the LAST
    /// Step — every earlier frame's contribution to accumulated state (an already-spawned emitter's own
    /// strength/radius, baked in at spawn time; the velocity field's accumulated history) still reflected the
    /// OLD value. One re-stepped frame barely moves cumulative state built from many frames under the old
    /// value, so parameters that mostly matter cumulatively (PixelFluidModifier's vortex/wave/viscosity
    /// sliders) looked almost inert while the preview was paused mid-clip, even though the underlying math was
    /// fine (confirmed by isolating each subsystem directly). A full replay from 0 re-derives EVERY frame,
    /// including every emitter's own spawn, under the CURRENT parameters — the only way to make an edited
    /// slider correctly reflect across the whole accumulated history, not just its most recent frame.
    public abstract class SimulationModifier : PyreModifier
    {
        // -1 means "never initialized" — deliberately distinct from "initialized, currently AT frame 0", so the
        // very first EnsureFrame call (however it comes in) always takes the ResetState+replay branch below
        // instead of mistaking cachedFrame+1==0 for "frame 0 is the cheap next-step of an existing state".
        [NonSerialized] protected int cachedFrame = -1;
        [NonSerialized] int cachedW = -1, cachedH = -1;
        /// This blast's seed, set by BlastRenderer right before Prepare — mirrors PostModifier.life/SetLife.
        /// Concrete subclasses needing their own deterministic randomness seed a custom PRNG from this rather
        /// than UnityEngine.Random/System.Random.
        [NonSerialized] protected int seed;
        internal void SetSeed(int s) => seed = s;
        protected static byte ToByte(float v) => (byte)(Mathf.Clamp01(v) * 255f + 0.5f);

        /// Hard-reset all internal state, seeding it from `seedBuf` (the fully-composited buffer at the frame
        /// this reset targets — normally frame 0). Called once before replaying Step() from scratch.
        protected abstract void ResetState(Color32[] seedBuf, int W, int H);
        /// Advance the simulation by exactly one frame, using this instance's OWN just-Prepare()d parameters.
        protected abstract void Step(int frameIndex, Color32[] seedBuf, int W, int H);
        /// Paint the CURRENT internal state into the frame buffer. Called on every request (cheap — no
        /// simulation work here), always reflecting the latest state and parameters.
        public abstract void Render(Color32[] buf, int W, int H);

        /// `paramsForFrame(f)` resolves THIS modifier's animatable fields as frame f would have seen them — needed
        /// because a replay-from-scratch (any jump, including the very first-ever call) re-Steps every frame from
        /// 0 up to the target, and each of THOSE historical frames must use its OWN parameter values, not just
        /// whatever the caller most recently resolved for the frame it actually asked for. Without this, a cold
        /// scrub straight to frame 40 would replay frames 0..39 using frame 40's own (e.g.) Depth/Strength — wrong
        /// for any animated field. BlastRenderer passes a small closure here instead of calling Prepare itself;
        /// EnsureFrame calls Prepare(paramsForFrame(f)) immediately before every Step(f, ...), including the
        /// cheap path, so by the time this returns the instance's fields always reflect frameIndex's own values.
        internal void EnsureFrame(int frameIndex, Color32[] seedBuf, int W, int H, Func<int, Func<ZUIValue, int, float>> paramsForFrame)
        {
            if (frameIndex < 0) frameIndex = 0;
            bool sizeChanged = W != cachedW || H != cachedH;
            if (!sizeChanged && cachedFrame >= 0 && frameIndex == cachedFrame + 1)
            {
                Prepare(paramsForFrame(frameIndex));
                Step(frameIndex, seedBuf, W, H);
                cachedFrame = frameIndex;
            }
            else
            {
                ResetState(seedBuf, W, H);
                cachedFrame = -1;
                cachedW = W; cachedH = H;
                for (int f = 0; f <= frameIndex; f++)
                {
                    Prepare(paramsForFrame(f));
                    Step(f, seedBuf, W, H);
                    cachedFrame = f;
                }
            }
        }
    }

    /// The genuinely-iterative sibling BallisticShockwaveModifier's closed-form trick couldn't deliver: this one
    /// keeps REAL persisted state — a velocity displacement field and an alpha-erosion field, both sized to the
    /// canvas — that carries frame to frame, modelled on a reference PixelFluidSimulation (a Python density/
    /// velocity grid sim with a projectile tunnel, trailing shockwave rings, and an alternating vortex street).
    /// A second shockwave crossing an already-eroded patch genuinely digs it deeper here; a vortex's drift is a
    /// real integrated position, not re-derived from "how old is it" — the whole point of building
    /// SimulationModifier above. This is the ONE modifier living in Pyre.simulationModifier rather than an
    /// ordinary modifier list (see SimulationModifier's own doc comment for why that's safe).
    ///
    /// Vortices can shed a smaller child of their own, mirroring the reference's stochastic branching — safe
    /// here (unlike in the closed-form siblings) because the randomness comes from FluidRandom, a tiny explicit
    /// ulong-state PRNG reset alongside everything else in ResetState, so replaying from frame 0 reproduces the
    /// exact same spawn sequence every time (System.Random carries hidden global state that wouldn't).
    [Serializable]
    public class PixelFluidModifier : SimulationModifier
    {
        [Range(-180f, 180f)]
        [Tooltip("Direction the projectile travels, in degrees (0 = along +X). Animatable.")]
        public ZUIValue angleDeg = new ZUIValue(0f);
        [Range(-32f, 32f)]
        [Tooltip("Slides the travel line sideways (perpendicular to its own direction), in pixels off the " +
                 "canvas centre. Animatable.")]
        public ZUIValue offset = new ZUIValue(0f);
        [Range(0f, 1f)]
        [Tooltip("How far the projectile has travelled: 0 = hasn't entered yet, 1 = reached the far edge. " +
                 "Animatable — default ramps 0→1 over life.")]
        public ZUIValue depth = Layer.CurveVal(1f, 0f, 0f, 1f, 1f);
        [Range(0.5f, 10f)]
        [Tooltip("Radius of the projectile's own tunnel through the cloud, in pixels.")]
        public ZUIValue projectileRadius = new ZUIValue(3f);
        [Range(-20f, 20f)]
        [Tooltip("How hard the tunnel injects velocity (forward + sideways) into the persisted field, every " +
                 "frame it's moving through. Animatable.")]
        public ZUIValue projectileForce = new ZUIValue(10f);
        [Range(0f, 1f)]
        [Tooltip("How much alpha the tunnel's own core erodes per frame it's passing through — erosion " +
                 "PERSISTS and compounds (see Erosion healing below), it isn't re-derived fresh each frame. Animatable.")]
        public ZUIValue erosionRate = new ZUIValue(0.35f);
        [Range(0f, 1f)]
        [Tooltip("Fraction of accumulated erosion that heals back each frame — lower heals faster (a wake that " +
                 "closes back up quickly); 1 = permanent scarring, never heals.")]
        public ZUIValue erosionHealing = new ZUIValue(0.85f);

        [Range(0.01f, 0.5f)]
        [Tooltip("How often a shockwave ring spawns, as a FRACTION of the whole travel.")]
        public ZUIValue waveSpacing = new ZUIValue(0.06f);
        [Range(-20f, 20f)]
        [Tooltip("Each ring's push strength the moment it spawns, injected into the velocity field every frame " +
                 "it's alive. Animatable.")]
        public ZUIValue waveStrength = new ZUIValue(6f);
        [Range(0f, 4f)]
        [Tooltip("How far a ring's own radius grows each frame, in pixels — a real per-frame accumulation, not " +
                 "closed-form against age.")]
        public ZUIValue waveExpansion = new ZUIValue(1.5f);
        [Range(0f, 1f)]
        [Tooltip("Fraction of a ring's strength that survives each frame (persisted, multiplicative) — higher = " +
                 "longer-lived rings.")]
        public ZUIValue wavePersistence = new ZUIValue(0.9f);
        [Range(0.5f, 8f)]
        [Tooltip("Thickness of the travelling pressure shell, in pixels.")]
        public ZUIValue waveThickness = new ZUIValue(2.5f);

        [Range(0.01f, 0.5f)]
        [Tooltip("How often a vortex spawns, as a FRACTION of the whole travel — alternates spin by index, the " +
                 "same alternating \"vortex street\" a real bluff body sheds.")]
        public ZUIValue vortexSpacing = new ZUIValue(0.05f);
        [Range(-20f, 20f)]
        [Tooltip("Each vortex's swirl strength the moment it spawns, injected into the velocity field every " +
                 "frame it's alive. Animatable.")]
        public ZUIValue vortexStrength = new ZUIValue(10f);
        [Range(0.5f, 16f)]
        [Tooltip("Each vortex's core radius, in pixels.")]
        public ZUIValue vortexRadius = new ZUIValue(8f);
        [Range(0f, 1f)]
        [Tooltip("Fraction of a vortex's strength that survives each frame (persisted, multiplicative).")]
        public ZUIValue vortexPersistence = new ZUIValue(0.94f);
        [Range(-5f, 5f)]
        [Tooltip("Per-frame drift speed of a vortex's own centre, in pixels/frame — a genuinely integrated " +
                 "position (carried and accumulated frame to frame), not re-derived from its age.")]
        public ZUIValue vortexDrift = new ZUIValue(0.6f);
        [HideInInspector] public float childShedChance = 0.05f;   // FROZEN legacy source (task: modifier MultiCont overhaul) — never rename/retype
        [Range(0f, 0.5f)]
        [Tooltip("Chance, each frame, that an existing vortex sheds a smaller child of its own — a real random " +
                 "draw from this modifier's own snapshot-able PRNG (seeded from the blast's seed), scaled down " +
                 "by the vortex's own remaining strength so young vortices shed more than fading ones.")]
        public ZUIValue childShedChanceValue = new ZUIValue(0.05f);
        [HideInInspector] public bool childShedChanceUpgraded;
        public ZUIValue ChildShedChance { get { if (!childShedChanceUpgraded) { childShedChanceValue = new ZUIValue(childShedChance); childShedChanceUpgraded = true; } return childShedChanceValue; } }

        [Range(0f, 1f)]
        [Tooltip("Fraction of the velocity field that survives each frame (persisted, multiplicative drag).")]
        public ZUIValue velocityDrag = new ZUIValue(0.85f);
        [Range(0f, 1f)]
        [Tooltip("How much the velocity field blurs into its own neighbours each frame (0 = no diffusion, 1 = " +
                 "fully smoothed) — a cheap viscosity.")]
        public ZUIValue viscosity = new ZUIValue(0.25f);
        [Range(0f, 5f)]
        [Tooltip("How strongly the persisted velocity field displaces pixels when rendered.")]
        public ZUIValue displayScale = new ZUIValue(1f);

        // Hard cap on concurrently-tracked waves/vortices — an internal quality/perf knob, not a creative param.
        const int MaxEmitters = 40;
        struct WaveState { public float along, radius, strength; }
        struct VortexState { public Vector2 center, vel; public float radius, strength, spin, phase; }

        // Explicit ulong state (not System.Random) so a full replay from frame 0 always draws the exact same
        // sequence, regardless of how many times this instance has been replayed before.
        struct FluidRandom
        {
            ulong state;
            public FluidRandom(int seed) { state = unchecked((ulong)seed * 0x9E3779B97F4A7C15UL + 1UL); }
            public float Next01()
            {
                state += 0x9E3779B97F4A7C15UL;
                ulong z = state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                z ^= z >> 31;
                return (float)((z >> 40) / (double)(1UL << 24));
            }
        }

        Vector2[] velocity;
        float[] erosion;
        List<WaveState> waves = new List<WaveState>();
        List<VortexState> vortices = new List<VortexState>();
        FluidRandom rng;
        Vector2 lastPos;
        float lastAlong, lastWaveAlong, lastVortexAlong, nextVortexSpin;
        int stateW, stateH;

        float ang, offPx, depthV, projRad, projForce, erosionRateV, erosionHealV;
        float waveSpacingF, waveStr, waveExp, wavePersist, waveThick;
        float vortexSpacingF, vortexStr, vortexRad, vortexPersist, vortexDriftV, shedChanceV;
        float drag, visc, dispScale;

        public override string DisplayName => "Pixel fluid";

        public override void Prepare(Func<ZUIValue, int, float> e)
        {
            ang = e(angleDeg, 0) * Mathf.Deg2Rad;
            offPx = e(offset, 1);
            depthV = Mathf.Clamp01(e(depth, 2));
            projRad = Mathf.Max(0.5f, e(projectileRadius, 3));
            projForce = e(projectileForce, 4);
            erosionRateV = Mathf.Clamp01(e(erosionRate, 5));
            erosionHealV = Mathf.Clamp01(e(erosionHealing, 6));
            waveSpacingF = Mathf.Max(0.005f, e(waveSpacing, 7));
            waveStr = e(waveStrength, 8);
            waveExp = e(waveExpansion, 9);
            wavePersist = Mathf.Clamp01(e(wavePersistence, 10));
            waveThick = Mathf.Max(0.5f, e(waveThickness, 11));
            vortexSpacingF = Mathf.Max(0.005f, e(vortexSpacing, 12));
            vortexStr = e(vortexStrength, 13);
            vortexRad = Mathf.Max(0.5f, e(vortexRadius, 14));
            vortexPersist = Mathf.Clamp01(e(vortexPersistence, 15));
            vortexDriftV = e(vortexDrift, 16);
            drag = Mathf.Clamp01(e(velocityDrag, 17));
            visc = Mathf.Clamp01(e(viscosity, 18));
            dispScale = e(displayScale, 19);
            // fid 20 extends this modifier's existing 0-19 run — benign: the fid only seeds a param's Min-Max
            // RNG stream.
            shedChanceV = Mathf.Clamp(e(ChildShedChance, 20), 0f, 0.5f);
        }

        protected override void ResetState(Color32[] seedBuf, int W, int H)
        {
            stateW = W; stateH = H;
            velocity = new Vector2[W * H];
            erosion = new float[W * H];
            waves.Clear();
            vortices.Clear();
            rng = new FluidRandom(seed);

            float reach = ComputeCanvasReach(ang, W, H);
            Vector2 canvasCenter = new Vector2(W * 0.5f, H * 0.5f);
            Vector2 dir0 = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            Vector2 perp0 = new Vector2(-dir0.y, dir0.x);
            lastAlong = -reach;
            lastWaveAlong = -reach;
            lastVortexAlong = -reach;
            nextVortexSpin = 1f;
            lastPos = canvasCenter + perp0 * offPx + dir0 * lastAlong;
        }


        protected override void Step(int frameIndex, Color32[] seedBuf, int W, int H)
        {
            Vector2 canvasCenter = new Vector2(W * 0.5f, H * 0.5f);
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            Vector2 perp = new Vector2(-dir.y, dir.x);
            Vector2 pivot = canvasCenter + perp * offPx;
            float reach = ComputeCanvasReach(ang, W, H);
            float along = Mathf.Lerp(-reach, reach, depthV);
            Vector2 posNow = pivot + dir * along;
            float spacingPx = Mathf.Max(1f, 2f * reach);

            InjectProjectile(pivot, dir, perp, lastPos, posNow, W, H);

            if (along - lastWaveAlong >= waveSpacingF * spacingPx)
            {
                if (waves.Count >= MaxEmitters) waves.RemoveAt(0);
                waves.Add(new WaveState { along = along, radius = 0.5f, strength = waveStr });
                lastWaveAlong = along;
            }
            if (along - lastVortexAlong >= vortexSpacingF * spacingPx)
            {
                SpawnVortex(pivot, dir, perp, along, nextVortexSpin);
                nextVortexSpin = -nextVortexSpin;
                lastVortexAlong = along;
            }

            UpdateWaves(pivot, dir, W, H);
            UpdateVortices(W, H);
            DecayAndDiffuseField(W, H);

            lastPos = posNow;
            lastAlong = along;
        }

        void InjectProjectile(Vector2 pivot, Vector2 dir, Vector2 perp, Vector2 from, Vector2 to, int W, int H)
        {
            if (projForce == 0f && erosionRateV <= 0.0001f) return;
            float startAlong = Vector2.Dot(from - pivot, dir);
            float endAlong = Vector2.Dot(to - pivot, dir);
            float lo = Mathf.Min(startAlong, endAlong), hi = Mathf.Max(startAlong, endAlong);

            Vector2 loP = pivot + dir * lo, hiP = pivot + dir * hi;
            int xlo = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(loP.x, hiP.x) - projRad), 0, W - 1);
            int xhi = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(loP.x, hiP.x) + projRad), 0, W - 1);
            int ylo = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(loP.y, hiP.y) - projRad), 0, H - 1);
            int yhi = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(loP.y, hiP.y) + projRad), 0, H - 1);

            for (int y = ylo; y <= yhi; y++)
                for (int x = xlo; x <= xhi; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    Vector2 d = p - pivot;
                    float alongSigned = Vector2.Dot(d, dir);
                    float nearestAlong = Mathf.Clamp(alongSigned, lo, hi);
                    Vector2 toPixel = p - (pivot + dir * nearestAlong);
                    float dist = toPixel.magnitude;
                    if (dist >= projRad) continue;
                    float falloff = 1f - dist / projRad;
                    Vector2 sideDir = dist > 0.001f ? toPixel / dist : perp;
                    int idx = y * W + x;
                    velocity[idx] += dir * (projForce * falloff * 0.4f) + sideDir * (projForce * falloff);
                    erosion[idx] = Mathf.Min(1f, erosion[idx] + erosionRateV * falloff);
                }
        }

        void SpawnVortex(Vector2 pivot, Vector2 dir, Vector2 perp, float along, float spin)
        {
            if (vortices.Count >= MaxEmitters) vortices.RemoveAt(0);
            Vector2 center = pivot + dir * along + perp * (spin * vortexRad * 0.6f);
            vortices.Add(new VortexState
            {
                center = center,
                vel = perp * (spin * vortexDriftV),
                radius = vortexRad,
                strength = vortexStr,
                spin = spin,
                phase = 0f
            });
        }

        void UpdateWaves(Vector2 pivot, Vector2 dir, int W, int H)
        {
            for (int i = waves.Count - 1; i >= 0; i--)
            {
                var w = waves[i];
                w.radius += waveExp;
                w.strength *= wavePersist;
                if (w.strength < 0.05f) { waves.RemoveAt(i); continue; }
                waves[i] = w;
            }
            for (int i = 0; i < waves.Count; i++)
            {
                var w = waves[i];
                Vector2 centerP = pivot + dir * w.along;
                float reachR = w.radius + waveThick;
                int xlo = Mathf.Clamp(Mathf.FloorToInt(centerP.x - reachR), 0, W - 1);
                int xhi = Mathf.Clamp(Mathf.CeilToInt(centerP.x + reachR), 0, W - 1);
                int ylo = Mathf.Clamp(Mathf.FloorToInt(centerP.y - reachR), 0, H - 1);
                int yhi = Mathf.Clamp(Mathf.CeilToInt(centerP.y + reachR), 0, H - 1);
                for (int y = ylo; y <= yhi; y++)
                    for (int x = xlo; x <= xhi; x++)
                    {
                        Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                        Vector2 d = p - centerP;
                        float dist = d.magnitude;
                        if (dist < 0.001f) continue;
                        float distToRing = Mathf.Abs(dist - w.radius);
                        if (distToRing > waveThick) continue;
                        float shell = 1f - distToRing / waveThick;
                        int idx = y * W + x;
                        velocity[idx] += (d / dist) * (w.strength * shell * 0.2f);
                        erosion[idx] = Mathf.Min(1f, erosion[idx] + shell * w.strength * 0.01f);
                    }
            }
        }

        void UpdateVortices(int W, int H)
        {
            int existing = vortices.Count;
            for (int i = existing - 1; i >= 0; i--)
            {
                var v = vortices[i];
                v.center += v.vel;
                v.strength *= vortexPersist;
                v.phase += 0.3f;
                if (v.strength < 0.05f) { vortices.RemoveAt(i); continue; }
                vortices[i] = v;

                float shedChance = shedChanceV * Mathf.Clamp01(v.strength / Mathf.Max(0.01f, vortexStr));
                if (shedChance > 0f && rng.Next01() < shedChance && vortices.Count < MaxEmitters)
                {
                    float childSpin = rng.Next01() < 0.5f ? v.spin : -v.spin;
                    Vector2 jitter = new Vector2(rng.Next01() - 0.5f, rng.Next01() - 0.5f) * (vortexDriftV * 0.5f);
                    vortices.Add(new VortexState
                    {
                        center = v.center,
                        vel = v.vel + jitter,
                        radius = v.radius * 0.55f,
                        strength = v.strength * 0.5f,
                        spin = childSpin,
                        phase = v.phase
                    });
                }
            }

            for (int i = 0; i < vortices.Count; i++)
            {
                var v = vortices[i];
                float reachV = v.radius * 1.5f;
                int xlo = Mathf.Clamp(Mathf.FloorToInt(v.center.x - reachV), 0, W - 1);
                int xhi = Mathf.Clamp(Mathf.CeilToInt(v.center.x + reachV), 0, W - 1);
                int ylo = Mathf.Clamp(Mathf.FloorToInt(v.center.y - reachV), 0, H - 1);
                int yhi = Mathf.Clamp(Mathf.CeilToInt(v.center.y + reachV), 0, H - 1);
                for (int y = ylo; y <= yhi; y++)
                    for (int x = xlo; x <= xhi; x++)
                    {
                        Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                        Vector2 d = p - v.center;
                        float dist = d.magnitude;
                        if (dist < 0.001f || dist > reachV) continue;
                        float nd = dist / v.radius;
                        float falloff = Mathf.Exp(-nd * nd * 1.6f);
                        Vector2 normal = d / dist;
                        Vector2 tangent = new Vector2(-normal.y, normal.x) * v.spin;
                        int idx = y * W + x;
                        velocity[idx] += tangent * (v.strength * falloff * 0.2f);
                    }
            }
        }

        void DecayAndDiffuseField(int W, int H)
        {
            int n = W * H;
            if (visc > 0.001f)
            {
                var blurred = new Vector2[n];
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int idx = y * W + x;
                        Vector2 sum = velocity[idx];
                        int cnt = 1;
                        if (x > 0) { sum += velocity[idx - 1]; cnt++; }
                        if (x < W - 1) { sum += velocity[idx + 1]; cnt++; }
                        if (y > 0) { sum += velocity[idx - W]; cnt++; }
                        if (y < H - 1) { sum += velocity[idx + W]; cnt++; }
                        blurred[idx] = Vector2.Lerp(velocity[idx], sum / cnt, visc);
                    }
                Array.Copy(blurred, velocity, n);
            }
            for (int i = 0; i < n; i++)
            {
                velocity[i] *= drag;
                erosion[i] *= erosionHealV;
            }
        }

        public override void Render(Color32[] buf, int W, int H)
        {
            if (velocity == null || velocity.Length != W * H) return;   // never stepped this size — leave untouched
            var cloud = (Color32[])buf.Clone();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int idx = y * W + x;
                    Vector2 disp = velocity[idx] * dispScale;
                    Vector2 sourcePos = new Vector2(x + 0.5f, y + 0.5f) - disp;
                    Color32 sampled = SampleNearestLocal(cloud, W, H, sourcePos);
                    float ero = erosion[idx];
                    if (ero > 0.001f)
                        sampled.a = ToByte(sampled.a * (1f / 255f) * (1f - ero));
                    buf[idx] = sampled;
                }
        }

        // Runtime simulation state (grids/lists/PRNG) is a live-running cache, not authored data — MemberwiseClone
        // (the base Clone()'s first step) shares these list/array REFERENCES between original and copy, so
        // without this override, mutating a "Dup"'d modifier's simulation would silently corrupt the original's
        // too. Resetting to fresh/empty here (rather than deep-copying) is fine: cachedFrame = -1 forces the very
        // next EnsureFrame call to rebuild everything from ResetState anyway.
        public override PyreModifier Clone()
        {
            var m = (PixelFluidModifier)base.Clone();
            m.velocity = null;
            m.erosion = null;
            m.waves = new List<WaveState>();
            m.vortices = new List<VortexState>();
            m.rng = default;
            m.stateW = 0; m.stateH = 0;
            m.cachedFrame = -1;
            return m;
        }

        static float ComputeCanvasReach(float ang, int W, int H)
        {
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            float hHalf = W * 0.5f, vHalf = H * 0.5f;
            float rx = Mathf.Abs(dir.x) > 1e-4f ? hHalf / Mathf.Abs(dir.x) : float.MaxValue;
            float ry = Mathf.Abs(dir.y) > 1e-4f ? vHalf / Mathf.Abs(dir.y) : float.MaxValue;
            return Mathf.Min(rx, ry);
        }

        static Color32 SampleNearestLocal(Color32[] buf, int W, int H, Vector2 p)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt(p.x), 0, W - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(p.y), 0, H - 1);
            return buf[y * W + x];
        }
    }

}
