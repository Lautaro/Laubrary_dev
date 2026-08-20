// PyrePlusRenderer — the parallel renderer for PyrePlusSpec (see PYREPLUS_DESIGN.md).
//
// Mirrors BlastRenderer's determinism contract exactly: pure, static, every random value derived from a
// seeded System.Random keyed by (seed, particleIndex, fieldId, salt) — never UnityEngine.Random, never Time —
// so preview, bake and runtime all produce byte-identical output from the same inputs, and any frame renders
// standalone. Swarm off ⇒ a single centred particle through the Shape fields. Swarm on ⇒ N particles, each
// spawned at its own point on the blast timeline and placed by a spawn-time snapshot of the shape transform
// (T2: Area = uniform-by-area point inside Circle/regular-polygon; Path = a point on the outline positioned by
// swarmProgress, or the swarmCustomX/Y envelopes for a Custom polyline).
//
// T6 — Modifiers: reuses Pyre's own PyreModifier stack directly (each layer's modifiers list), applied exactly as
// BlastRenderer applies a layer's stack, adapted to PyrePlus's procedural discs. GeometryModifiers fold the
// sample position per candidate pixel (bending each disc) BEFORE the distance/edge test; PixelModifiers recolour
// or drop each lit pixel before it composites; PostModifiers run over the whole finished frame buffer in list
// order. When the list is empty / all-disabled, ALL modifier machinery is skipped and the output is
// byte-identical to the pre-T6 raster (the orchestrator hash-checks this).
using System.Collections.Generic;
using System.Reflection;
using Laubrary.Pyre;
using Laubrary.SpriteFx;
using TMPro;
using UnityEngine;

namespace Laubrary.PyrePlus
{
    public static class PyrePlusRenderer
    {
        static readonly Color32 Transparent = new Color32(0, 0, 0, 0);

        // ── field-ID registry ──────────────────────────────────────────────────────
        // Every ZUIValue Eval and every seeded random draw takes a UNIQUE field id so independent fields never
        // share an RNG stream (which would silently CORRELATE their draws — e.g. a MinMax size and a MinMax
        // placement landing in lock-step). Slice 1 used bare ints (size=1, alpha=2); these name them and RESERVE
        // the rest for later tasks. LATER TASKS MUST take their ids from this table — never invent a colliding
        // literal, and never reuse an id already spoken for.
        const int FldSize = 1;       // Shape size envelope, particle's own life          [T1]
        const int FldAlpha = 2;      // Shape alpha envelope, particle's own life         [T1]
        const int FldPlacement = 3;  // Area disc sampling (u1/u2 random draw)            [T1]
        const int FldScale = 4;      // shapeScale — shape radius, spawn-time snapshot    [T1]
        const int FldProgress = 5;   // swarmProgress — Path position, spawn-time snapshot [T2]
        const int FldOffsetX = 6;    // shapeOffsetX, spawn-time snapshot                 [T3]
        const int FldOffsetY = 7;    // shapeOffsetY, spawn-time snapshot                 [T3]
        const int FldRotation = 8;   // shapeRotation 2D, spawn-time snapshot             [T3]
        const int FldPitch = 9;      // shapePitch, spawn-time snapshot                   [T3]
        const int FldYaw = 10;       // shapeYaw, spawn-time snapshot                     [T3]
        const int FldSpin = 11;      // per-particle 2D spin, particle's own life         [T7]
        const int FldPathX = 12;     // per-particle position-path X, own life            [T7]
        const int FldPathY = 13;     // per-particle position-path Y, own life            [T7]
        const int FldCustomX = 14;   // swarmCustomX — Custom Path X(progress), sampled at progress p [T2]
        const int FldCustomY = 15;   // swarmCustomY — Custom Path Y(progress), sampled at progress p [T2]
        // NEGATIVE-ID RULE for NEW single fields: ids 1..15 above are taken and 16+ belongs to the per-modifier
        // 8-wide blocks (below), so any brand-new single-field id must be NEGATIVE (-2 … -18 are spoken for; use
        // -19 onward). The value -1 is
        // already spoken for by ModParticleIndex, but that is a *particleIndex* (the `b` slot of Hash), a
        // DIFFERENT argument slot from a field id (the `c` slot); a negative FIELD id can therefore never collide
        // with the -1 particle sentinel, nor with any positive field/modifier id. Real particles are 0..N-1, so a
        // field keyed (particle i≥0, fid<0) shares no RNG stream with a modifier's (particle -1, fid≥16) draw.
        const int FldSpawnTiming = -2;   // swarmSpawnTiming — remaps a particle's number → its spawn moment [F3]
        const int FldGemTilt = -3;       // gemTilt — Gem world tilt about X, particle's own life          [Gem]
        const int FldGemEdgeGlow = -4;   // gemEdgeGlow — Gem edge-halo pulse strength, particle's own life [Gem]
        const int FldGemInnerGlow = -5;  // gemInnerGlow — Gem facet inner-glow pulse strength, own life     [Gem]
        const int FldCrescentBite = -6;  // crescentBite — Crescent mask-disc size, particle's own life      [G2]
        const int FldCrescentAngle = -7; // crescentAngle — Crescent bite facing, particle's own life         [G2]
        const int FldSparkle = -8;       // sparkleDensity Eval + the per-cell presence/twinkle Hash draws     [G2]
        const int FldGemRoll = -9;       // gemRoll — solid ROLL about Z (model space, first), particle's own life [R2]
        const int FldStreakLen = -10;    // streakLength — Streak forward length, particle's own life              [S1]
        const int FldStreakWidth = -11;  // streakWidth — Streak cross width, particle's own life                  [S1]
        const int FldScaleByIndex = -12; // swarmScaleByIndex — per-particle size multiplier by index (input i/(n-1)) [S1]
        const int FldStarLen = -13;      // starLength — Star arm-tip reach (inner radius = R·(1−len)), own life     [V1]
        const int FldStarBase = -14;     // starBaseWidth — Star valley angular position within its sector, own life  [V1]
        const int FldStarSkew = -15;     // starSkew — Star valley swirl (pinwheel), own life                         [V1]
        const int FldSwarmTurn = -16;    // swarmTurn — whole-cloud live yaw at the frame's life (frame-global)       [Issue2]
        const int FldSwarmTilt = -17;    // swarmTilt — whole-cloud live pitch at the frame's life (frame-global)     [Issue2]
        const int FldSwarmRoll = -18;    // swarmRoll — whole-cloud live roll at the frame's life (frame-global)      [Issue2]
        // Luma-matte (slice 4) — the four amount envelopes a LumaMatte layer Eval's per frame over its OWN life,
        // frame-global (the modifier-scope sentinel particle index). Reserved for exactly this since the registry
        // note above (-19..-22). strength is Clamp01'd; blur/displace/hue feed ApplyMatte's channel math.
        const int FldMatteStrength = -19; // matteStrength — LumaMatte master mask strength, matte layer's own life  [slice4]
        const int FldMatteBlur = -20;     // matteBlurAmount — LumaMatte Blur channel max radius                     [slice4]
        const int FldMatteDisplace = -21; // matteDisplaceAmount — LumaMatte Displace channel amount                 [slice4]
        const int FldMatteHue = -22;      // matteHueDegrees — LumaMatte Hue channel rotation                        [slice4]
        const int FldSwarmScale = -23;   // swarmScale — whole-cloud live uniform radial scale at the frame's life (frame-global) [slice0]
        const int FldFuseHash = -24;     // Fuse (Coalesce) field-pass — its per-LAYER hash for pixel-modifier / noise draws [slice1]
        // (fuseThreshold/fuseShadeRange/fuseSoftness are plain floats, NOT Eval'd — they need no field id; -24 is the
        //  one id the Fuse pass adds, a frame-global per-layer hash seed.)
        const int FldRampDensity = -25;  // density (mass) envelope — Ramp field-pass, particle's own life           [slice2]
        const int FldRampHeat = -26;     // heat (height/energy) envelope — Ramp field-pass, particle's own life      [slice2]
        const int FldRampHash = -27;     // Ramp field-pass per-LAYER hash — pixel-modifier draws + surface-noise seed [slice2]
        // (the Ramp knobs rampFusion/rampCoverage/rampLighting/rampRelief/rampLightAngle/rampRimScale are plain
        //  floats, NOT Eval'd — no field ids; the Ramp pass Eval's only density (-25) and heat (-26) per particle and
        //  adds one frame-global per-layer hash (-27), the twin of Fuse's -24. Next new single field id: -28 onward.)
        const int FldBorderWidth = -28;  // borderWidth — first-class border rim thickness (px), frame-global over the layer's life [task #60].
        const int FldEdgeSoftness = -29; // edgeSoftness — soft-rim fraction, animatable over the particle's own life [task #12].
        const int FldCrescentCenterX = -30; // crescentCenterXAnim — Crescent mask-disc centre X (radius units), own life [task #12]
        const int FldCrescentCenterY = -31; // crescentCenterYAnim — Crescent mask-disc centre Y (radius units), own life [task #12]
        // ── plug-in form dials (PlusForm) — an AUTO-DERIVED block, never hand-registered ───────────────────────
        // A form's Prepare gets Eval(value, slot) and slot s maps to FldForm - s (-1000, -1001, …, counting DOWN). The
        // block sits far below every hand-registered negative single id (-2..-58) and, being negative, can never meet
        // the positive modifier blocks (16 + idx*8), the Fire/Fireball 100..125 ids or the sim slot at 6416. One
        // layer has exactly one form, so two forms never share a block; a form's dials are decorrelated from each
        // other by slot exactly as a modifier's are by fid. Public: PlusFormPrepareCtx does the slot arithmetic.
        public const int FldForm = -1000;
        // (Gem also REUSES existing ids: FldSize for its radius R, FldAlpha for its output alpha, FldSpin for its
        //  3D yaw (Turn), FldGemTilt for its tilt, FldGemRoll for its roll, and FldPathX/FldPathY for the shared
        //  travel offset — no other new ids. Crescent/Sparkle/Sprite likewise REUSE FldSize/FldAlpha (radius/alpha),
        //  FldSpin (2D spin), FldPathX/Y (travel); Sprite needs no new random-draw id at all. Orb/Ring reuse the
        //  SAME Gem ids — FldSize (radius/outer radius), FldAlpha, FldSpin (Orb: lighting-frame yaw / Ring: in-plane
        //  yaw), FldGemTilt, FldGemRoll (Orb: lighting-frame roll; Ring: geometrically inert), FldGemEdgeGlow,
        //  FldGemInnerGlow, FldPathX/Y — and add NO further ids: ringInner is a plain non-animatable float.)
        // Modifiers (T6): each PyreModifier in a layer's modifiers list owns an 8-wide field-id BLOCK starting at
        // FldModifier + listIndex*8, so a modifier's local field id (its Prepare's fid, 0..7) maps to
        // FldModifier + listIndex*8 + fid. Since every modifier has a distinct list index the blocks never
        // overlap, and starting at 16 they never collide with the single-field ids 1..15 above. Mirrors
        // BlastRenderer's own `1000 + uid*8 + fid` keying, just rebased for PyrePlus.
        const int FldModifier = 16;
        // Modifier params are frame-global (not per-particle), so their Eval uses this sentinel particle index —
        // real particles are 0..N-1, so -1 never shares a Min-Max RNG stream with a particle draw. Mirrors
        // BlastRenderer feeding its GlobalLayerId (-1) as the layer id for global modifiers.
        public const int ModParticleIndex = -1;

        // ── Fire form field ids (shapeForm == Fire, slice 6a) — REUSE Pyre's own F_Fire* block VERBATIM ──────────
        // These are Pyre's exact fire field ids (Runtime/Pyre/BlastRenderer.cs:47-50), reused so a converted Pyre
        // fire keeps identical decorrelation and the port reads 1:1 against Pyre's FireParamsAt. POSITIVE 100..118 —
        // clear of PyrePlus's 1..15 single-field ids and its negative -2..-27 ids. (A layer's modifier BLOCK,
        // FldModifier + i*8, only reaches 100 at 11+ modifiers; benign: fire rates Eval frame-global at
        // ModParticleIndex exactly like modifier params, and a field id only perturbs a MinMax draw — which fire
        // rates and modifier params rarely use — so at worst two never-both-MinMax fields share a stream. Never a
        // determinism break: the replay gate proves same seed+content+frame ⇒ same bytes regardless.)
        const int F_FireDir = 100, F_FireWidth = 101, F_FireInset = 102, F_FireHeat = 103, F_FireFuel = 104;
        const int F_FirePulse = 105, F_FireFlow = 106, F_FireBuoy = 107, F_FireCurl = 108, F_FireCurlScale = 109;
        const int F_FireFlicker = 110, F_FireDissip = 111, F_FireBurn = 112, F_FireReach = 113, F_FireEdgeCool = 114;
        const int F_FireStretch = 115, F_FirePinch = 116, F_FireBreakup = 117, F_FireIntensity = 118;

        // ── Fireball form field ids (shapeForm == Fireball, slice 6b) — REUSE Pyre's own F_Fireball* block VERBATIM ──
        // Pyre's exact fireball field ids (Runtime/Pyre/BlastRenderer.cs:51), reused so a converted Pyre fireball keeps
        // identical decorrelation and this port reads 1:1 against Pyre's StepFireball. POSITIVE 120..125 — clear of
        // Fire's 100..118, PyrePlus's 1..15 single-field ids, and its negative -2..-27 ids. (Same benign note as Fire:
        // a modifier BLOCK at FldModifier + i*8 only reaches 120 at 13+ modifiers, and a field id only perturbs a
        // MinMax draw — never a determinism break, proven by the replay gate: same seed+content+frame ⇒ same bytes.)
        const int F_FireballSource = 120, F_FireballRadius = 121, F_FireballCool = 122;
        const int F_FireballSpread = 123, F_FireballReach = 124, F_FireballSharp = 125;

        // ── SimulationModifier slot field-id base (layer.simulationModifier, slice 7) ────────────────────────────
        // The per-layer stateful sim modifier drives its OWN animatable ZUIValues through the shared Eval funnel, so it
        // needs a field-id base clear of everything else. FAR above the layer's stateless modifier blocks (FldModifier
        // + idx*8) and the Fire/Fireball 100..125 range, mirroring BlastRenderer's own 800*8 offset for its per-layer
        // sim (Runtime/Pyre/BlastRenderer.cs:162 — 1000 + 800*8 + fid). 16 + 800*8 = 6416, so the sim modifier's fids
        // 6416.. never share a MinMax RNG stream with a modifier block or a fire rate.
        const int FldSimModifier = FldModifier + 800 * 8;

        /// One swarm particle's spawn data: where it lands on the blast timeline and its absolute canvas-pixel
        /// position. RenderFrame consumes this; the preview overlay (T5) calls ComputeSpawns to draw a dot at
        /// each particle's ACTUAL computed spawn location.
        public struct SpawnPoint
        {
            public float spawnLife;   // this particle's spawn point on the blast timeline, [0..1]
            public Vector2 pos;       // absolute canvas-pixel position (origin = buffer's (0,0))
            public float zNorm;       // pseudo-3D depth after the shape tilt, in [-1..1]: +1 nearest the viewer,
                                      // -1 farthest, exactly 0 when untilted. Drives depth shading here (nearer =
                                      // bigger + brighter); T5's overlay depth-codes each dot with it too.
            public float orientDeg;   // per-particle facing (S1), degrees: the target direction's math angle (CCW
                                      // from +x, y-up). None ⇒ 0. Outward = angle centre→pos; PathTangent = the
                                      // outline tangent angle. The renderer folds it into each form's own rotation
                                      // (Streak forward, Disc/Sprite spin, Text/solid roll). 0 ⇒ no fold applied.
        }

        // The current layer's decorrelation salt — its index in spec.layers. Folded into the d-slot of every
        // seeded draw (Eval's Hash, the placement/sparkle/pHash draws) so two layers never share an RNG stream.
        // Layer 0 → 0, so a single-default-layer spec is byte-identical to the pre-R3 renderer. Set before each
        // layer renders; read synchronously by everything downstream (the same per-frame-static idiom as _textReady).
        static int _layerSalt;

        public static Color32[] RenderFrame(PyrePlusSpec spec, int frameIndex)
        {
            int W = spec != null ? spec.Width : 1;
            int H = spec != null ? spec.Height : 1;
            var buf = new Color32[W * H];

            // `life`/`frames` are needed both for the background fill below and by the layer loop later; compute
            // them once here (spec-null-safe — identical to the later value when spec != null).
            int frames = Mathf.Max(1, spec != null ? spec.frameCount : 1);
            float life = frames > 1 ? frameIndex / (float)(frames - 1) : 0f;

            // Background. EXACT GUARD: only when backgroundUseFill is on (and a fill exists) do we evaluate the
            // ZuiFill per pixel across the canvas (u,v in -1..1); otherwise every pixel is the flat `background`
            // clear exactly as before — the default asset (backgroundUseFill == false) is byte-identical.
            if (spec != null && spec.backgroundUseFill && spec.backgroundFill != null)
            {
                for (int y = 0; y < H; y++)
                {
                    float v = H > 1 ? (y + 0.5f) / H * 2f - 1f : 0f;
                    int rowBase = y * W;
                    for (int x = 0; x < W; x++)
                    {
                        float u = W > 1 ? (x + 0.5f) / W * 2f - 1f : 0f;
                        buf[rowBase + x] = (Color32)spec.backgroundFill.Evaluate(life, u, v);
                    }
                }
            }
            else
            {
                Color32 bg = spec != null ? (Color32)spec.background : Transparent;
                for (int i = 0; i < buf.Length; i++) buf[i] = bg;
            }
            if (spec == null || spec.layers == null) return buf;
            // Per-frame wobble phase handed to every GeometryModifier.InverseWarp — matches BlastRenderer.framePhase
            // (note the divide by `frames`, not frames-1, deliberately mirroring BlastRenderer). One phase per frame,
            // shared by every layer.
            float phase = frames > 1 ? (frameIndex / (float)frames) * Mathf.PI * 2f : 0f;

            // Four shared matte channels (W*H each), zero-initialised, only allocated when some enabled layer
            // actually uses them. A WriteMatte layer writes its coverage into channels[matteChannel]; a Draw layer
            // with clipByChannel >= 0 multiplies its alpha by that channel just before compositing. Layers process
            // in paint order (0 = back), so a Draw layer can only clip by a channel an EARLIER layer already wrote.
            var layers = spec.layers;
            bool anyMatte = false;
            for (int li = 0; li < layers.Count; li++)
            {
                var l0 = layers[li];
                if (l0 == null || !l0.enabled) continue;
                // Channels are also needed when a Draw layer is a slice-4b HEIGHTMAP CONSUMER (heightFromChannel ≥ 0):
                // it reads the fused channel a WriteMatte luminance layer below deposited. Default -1 ⇒ no change here.
                if (l0.matteRole == MatteRole.WriteMatte
                    || (l0.matteRole == MatteRole.Draw && l0.clipByChannel >= 0)
                    || (l0.matteRole == MatteRole.Draw && l0.heightFromChannel >= 0)) { anyMatte = true; break; }
            }
            float[][] channels = null;
            // Per-channel "was WRITTEN this frame" flags (#58 Fix 3), parallel to `channels`. A channel starts
            // UNWRITTEN; WriteMatteCoverage flips its flag. A Draw layer that clips by a channel NOTHING wrote must
            // NOT be zeroed by that empty channel (the "luma matte only works at Clip-by = None" bug) — it skips the
            // clip instead (see `hasClip`). A written-but-locally-zero channel still clips (that's real coverage).
            bool[] channelWritten = null;
            if (anyMatte) { channels = new float[4][]; channelWritten = new bool[4]; for (int c = 0; c < 4; c++) channels[c] = new float[W * H]; }

            // `bufDirty` guards the "first drawn layer renders straight into buf" path: the first Draw layer to
            // composite paints (and posts) directly onto the bg-filled output, exactly as the pre-R3 single-layer
            // renderer did — which is what keeps a one-layer spec byte-identical even with an opaque background +
            // a post modifier. Every subsequent Draw layer (or any clip/matte/post-after-something layer) isolates
            // into its own transparent scratch buffer and composites, so its post never re-touches what's below.
            bool bufDirty = false;

            // Luma-matte (slice 4) carried BY-REF through the layer loop, PARALLEL to the numbered-channel path above.
            // A LumaMatte-role layer builds this from its finished pixels (mask + flags + amounts + scope); each
            // subsequent Draw layer runs ApplyMatte with it before compositing. mask == null ⇒ no matte active (the
            // default for every existing spec, which has no LumaMatte layer ⇒ this stays inert and byte-identical).
            var matteState = default(MatteState);

            // First-class border draw-over-matte (task #65): borders of layers whose borderOverMatte is ON are NOT
            // folded into their layer (their fill feeds the matte/clip/composite alone); the border is collected here
            // and composited ON TOP of the whole finished stack below, in paint order. Lazily allocated ⇒ null for
            // every spec with no over-matte border (all existing specs) ⇒ the end-of-loop composite is skipped ⇒
            // byte-identical.
            List<Color32[]> deferredBorders = null;

            for (int li = 0; li < layers.Count; li++)
            {
                var layer = layers[li];
                if (layer == null || !layer.enabled) continue;
                _layerSalt = li;

                // First-class border (task #60): only the six FLAT 2D forms, and only when enabled. Gated so every
                // other form — and every border-off layer (the default) — takes the exact pre-border path below
                // (hasBorder false ⇒ no border buffer built, needScratch unchanged, nothing deferred ⇒ byte-identical).
                // A plug-in form owns its whole look (shapeForm is ignored while `form` is set), so the border rim —
                // a feature of the six flat enum forms — does not apply to it.
                bool hasBorder = layer.form == null && layer.borderEnabled && IsFlat2DBorderForm(layer.shapeForm);

                // ── per-layer lifetime window (#55) — ported 1:1 from Pyre1 (BlastRenderer.cs:218/565) ──────────
                // Resolve the window against the frame count: endFrame < 0 is the "last frame" SENTINEL ⇒ frames-1
                // (so a full-range window ends at frames-1 at ANY frameCount, never a hardcoded 15). A default layer
                // (0 / -1) resolves to [0, frames-1]. OUTSIDE the window the layer is INACTIVE — it contributes
                // nothing this frame (mirrors how Pyre1 blanks a layer outside its window). INSIDE, the layer's
                // life is lerped 0..1 across [start, end] EXACTLY as Pyre1 does —
                // Mathf.Clamp01((frame-start)/Max(1,end-start)) — which for the default full range is the old
                // frame/(frames-1) VERBATIM (byte-identical), and which remaps the whole swarm's spawn/particle-life
                // timing (all fraction-of-life over the layer clock) across the window for free. `life` above stays
                // the spec-level clock for the background fill; `layerLife` is what every per-layer draw reads.
                int winStart = Mathf.Clamp(layer.startFrame, 0, frames - 1);
                int winEnd = layer.endFrame < 0 ? (frames - 1) : Mathf.Clamp(layer.endFrame, 0, frames - 1);
                if (winEnd < winStart) winEnd = winStart;   // degenerate authoring collapses to a single live frame
                if (frameIndex < winStart || frameIndex > winEnd) continue;   // out of window ⇒ inactive this frame
                float layerLife = Mathf.Clamp01((frameIndex - winStart) / (float)Mathf.Max(1, winEnd - winStart));

                // Build + Prepare this layer's EFFECTIVE geometry/pixel modifier stack for THIS frame = its own
                // modifiers wrapped by the spec-wide globalModifiers in Pyre1's order (global geometry OUTERMOST,
                // global pixels AFTER the layer's own — see BuildMods/CollectMods). Layer mods Eval at layerLife,
                // globals at the whole-timeline blast `life`, mirroring BlastRenderer's lp vs bp. Empty on BOTH ⇒
                // ModSet.Empty ⇒ DrawParticle's byte-identical fast path; empty globals ⇒ this layer's stack is
                // exactly its own modifiers (byte-identical to pre-#56).
                ModSet mods = BuildMods(layer.modifiers, spec.globalModifiers, spec.seed, layerLife, life);
                bool hasPost = HasEnabledPost(layer.modifiers);
                // Simulation slot (slice 7): the layer's own STATEFUL modifier. Like Fire/Fireball it retains state and
                // must render into isolated scratch (its Render OVERWRITES/advects pixels — see ApplyLayerSim), so it
                // forces needScratch for the Draw path and runs in the sim slot (after the stateless posts, before the
                // matte). Default null ⇒ false ⇒ every existing spec byte-identical.
                bool hasLayerSim = layer.simulationModifier != null && layer.simulationModifier.enabled;
                // Text form: bake + snapshot the SDF atlas for THIS layer's string/font. _textReady gates every Text
                // draw; when false (no readable font) each Text particle falls back to the Disc raster instead.
                _textReady = layer.shapeForm == ShapeForm.Text && EnsureTextGlyphs(layer);

                // #57 — the ENTIRE matte block (write/luma role, numbered-channel clip, slice-4b height consume) is
                // gated on matteEnabled. When a layer's Matte is toggled OFF it acts as a plain Draw layer, so the
                // window no longer has to WIPE matteRole/clipByChannel/heightFromChannel to make a matte stop acting —
                // it just clears this flag and the data is PRESERVED for re-enabling. matteEnabled DEFAULTS TRUE (see
                // PyrePlusSpec), so every pre-existing matte spec — including a field-absent old asset whose matte role
                // predates this flag — stays enabled ⇒ byte-identical.
                bool matteOn = layer.matteEnabled;
                bool isMatte = matteOn && layer.matteRole == MatteRole.WriteMatte;
                bool isLuma = matteOn && layer.matteRole == MatteRole.LumaMatte;
                // #58 Fix 3: only clip when the target channel was actually WRITTEN by an earlier layer. Clip-by an
                // UNWRITTEN channel ⇒ hasClip false ⇒ clip == null ⇒ the layer composites normally (no all-zero blank).
                bool hasClip = matteOn && !isMatte && !isLuma && channels != null && layer.clipByChannel >= 0 && layer.clipByChannel < 4 && channelWritten[layer.clipByChannel];

                if (isMatte)
                {
                    // WriteMatte: render the layer into an isolated transparent scratch exactly as if drawn, run its
                    // own post pass on that scratch, then read each pixel's ALPHA (the coverage it WOULD have drawn)
                    // into its channel. Never composited — a matte layer is invisible.
                    var scratch = new Color32[W * H];
                    RenderLayer(scratch, W, H, layerLife, spec, layer, mods, phase, frameIndex);
                    // Border (task #60/#65): built from the FILL silhouette (pre-post). borderOverMatte OFF folds the
                    // rim into the coverage (part of the invisible matte); ON keeps the coverage fill-only and defers
                    // the rim to draw on TOP of the finished frame — so this matte's own shape can stencil a channel
                    // while its border stays visible (the #65 fix: no separate outline-only layer).
                    Color32[] borderBuf = hasBorder ? BuildBorderBuffer(scratch, W, H, layerLife, spec, layer) : null;
                    if (borderBuf != null && !layer.borderOverMatte) CompositeLayer(scratch, borderBuf, null, false);
                    if (hasPost) ApplyLayerPost(layer.modifiers, spec.seed, scratch, W, H, layerLife, frameIndex);
                    if (hasLayerSim) ApplyLayerSim(layer.simulationModifier, spec.seed, scratch, W, H, frameIndex, frames);
                    if (channels != null) { int wch = Mathf.Clamp(layer.matteChannel, 0, 3); WriteMatteCoverage(channels[wch], scratch, layer.matteCombine, layer.matteWriteLuma); channelWritten[wch] = true; }
                    if (borderBuf != null && layer.borderOverMatte) (deferredBorders ??= new List<Color32[]>()).Add(borderBuf);
                    continue;
                }

                if (isLuma)
                {
                    // LumaMatte (slice 4): render + post into an isolated scratch exactly like WriteMatte, but instead
                    // of writing a coverage channel, turn the finished pixels into a luminance×alpha MASK and ARM
                    // matteState for the Draw layers above (a PUSH matte — the authoritative matte layer imposes its
                    // channels on the passive layers on top). Never composited — a matte layer is invisible. The
                    // strength/amounts Eval over THIS layer's own life, frame-global (the modifier-scope sentinel).
                    var scratch = new Color32[W * H];
                    RenderLayer(scratch, W, H, layerLife, spec, layer, mods, phase, frameIndex);
                    // Border (task #60/#65): borderOverMatte OFF folds the rim into the luminance mask (part of the
                    // invisible matte); ON builds the mask from the fill alone and defers the rim on top of the frame.
                    Color32[] borderBuf = hasBorder ? BuildBorderBuffer(scratch, W, H, layerLife, spec, layer) : null;
                    if (borderBuf != null && !layer.borderOverMatte) CompositeLayer(scratch, borderBuf, null, false);
                    if (hasPost) ApplyLayerPost(layer.modifiers, spec.seed, scratch, W, H, layerLife, frameIndex);
                    if (hasLayerSim) ApplyLayerSim(layer.simulationModifier, spec.seed, scratch, W, H, frameIndex, frames);
                    float strength = Mathf.Clamp01(Eval(layer.matteStrength, layerLife, spec.seed, ModParticleIndex, FldMatteStrength));
                    matteState.mask = BuildMatteMask(scratch, layer.matteInvert, strength);
                    matteState.flags = layer.matteFlags;
                    matteState.blurAmt = Eval(layer.matteBlurAmount, layerLife, spec.seed, ModParticleIndex, FldMatteBlur);
                    matteState.dispAmt = Eval(layer.matteDisplaceAmount, layerLife, spec.seed, ModParticleIndex, FldMatteDisplace);
                    matteState.hueDeg = Eval(layer.matteHueDegrees, layerLife, spec.seed, ModParticleIndex, FldMatteHue);
                    matteState.oneShot = layer.matteScope == MatteScope.NextLayer;
                    matteState.alphaSource = layer.matteAlphaSource;   // slice 5 — decided by the matte layer
                    if (borderBuf != null && layer.borderOverMatte) (deferredBorders ??= new List<Color32[]>()).Add(borderBuf);
                    continue;
                }

                // Draw layer. Render straight into `buf` — matching the pre-R3 path — only when no isolation is
                // needed: no clip (which must multiply alpha before compositing), no active luma matte (which
                // rewrites this layer's own pixels before it composites), and it isn't a later layer whose post
                // would re-process the layers already beneath it. hasPost && !bufDirty means it's the FIRST thing on
                // the frame, so posting over buf == posting over (bg + this layer) == the old renderer. matteActive
                // is always false for a spec with no LumaMatte layer, so needScratch reduces to the exact pre-slice-4
                // expression and the fast path is byte-identical.
                // Heightmap consumer (slice 4b): a Draw layer with heightFromChannel ≥ 0 does NOT draw its shape — it
                // renders the FUSED scalar field in channels[heightFromChannel] (deposited by the WriteMatte luminance
                // layers below) as a relief-lit heightmap through its own Fill. Everything else (post / clip / matte /
                // composite / bufDirty) is the normal Draw path — only the shape-render call is swapped. Default -1 ⇒
                // isHeightConsumer false ⇒ the normal RenderLayer, byte-identical.
                bool isHeightConsumer = matteOn && channels != null && layer.heightFromChannel >= 0 && layer.heightFromChannel < 4;
                float[] heightField = isHeightConsumer ? channels[layer.heightFromChannel] : null;

                // Fire (slice 6a) — a stateful sim form MUST render into isolated scratch: FireSim.Render OVERWRITES
                // above-threshold pixels and leaves the rest untouched (it never clears), so painting it straight into
                // the shared output would replace rather than Over-composite it and would carry whatever was already
                // there. Forcing needScratch mirrors BlastRenderer's hasLayerPost isolation for Fire/Fireball. The
                // fire render itself is dispatched inside RenderLayer (the scratch branch below calls RenderLayer).
                bool isFire = layer.shapeForm == ShapeForm.Fire;
                // Fireball (slice 6b) — the second stateful sim form; same isolation reason as Fire (FireballSim.Render
                // OVERWRITES above-threshold pixels and never clears, so it must Over-composite from an isolated scratch,
                // never render straight into the shared output). Dispatched inside RenderLayer's scratch branch below.
                bool isFireball = layer.shapeForm == ShapeForm.Fireball;

                bool matteActive = matteState.mask != null;
                // hasBorder forces the isolated-scratch path so the border can read this layer's OWN fill alpha (the
                // straight-into-buf fast path has no separate layer buffer to rim). Border OFF ⇒ hasBorder false ⇒
                // needScratch is exactly the pre-border expression ⇒ the fast path is untouched and byte-identical.
                // A plug-in form is treated like the whole-layer forms: it may SET pixels over its silhouette, so it
                // always paints into an isolated scratch that is then Over-composited.
                bool isForm = layer.form != null;
                bool needScratch = hasClip || (hasPost && bufDirty) || matteActive || isFire || isFireball || isForm || hasLayerSim || hasBorder;
                if (!needScratch)
                {
                    if (isHeightConsumer) RenderHeightConsumer(buf, W, H, layer, heightField);
                    else RenderLayer(buf, W, H, layerLife, spec, layer, mods, phase, frameIndex);
                    if (hasPost) ApplyLayerPost(layer.modifiers, spec.seed, buf, W, H, layerLife, frameIndex);
                }
                else
                {
                    var scratch = new Color32[W * H];
                    if (isHeightConsumer) RenderHeightConsumer(scratch, W, H, layer, heightField);
                    else RenderLayer(scratch, W, H, layerLife, spec, layer, mods, phase, frameIndex);
                    // Border (task #60/#65): built from the fill silhouette (pre-post). OFF folds the rim into the
                    // layer (composited/clipped/matte'd with the fill like normal); ON composites the fill alone and
                    // defers the rim on top of the finished frame (an always-on-top outline).
                    Color32[] borderBuf = hasBorder ? BuildBorderBuffer(scratch, W, H, layerLife, spec, layer) : null;
                    if (borderBuf != null && !layer.borderOverMatte) CompositeLayer(scratch, borderBuf, null, false);
                    if (hasPost) ApplyLayerPost(layer.modifiers, spec.seed, scratch, W, H, layerLife, frameIndex);
                    // Simulation slot (slice 7): run the layer's stateful sim modifier HERE — after the stateless post
                    // modifiers, before the matte apply — on the isolated scratch, the exact sim-slot position vanilla
                    // Pyre uses per layer (Runtime/Pyre/BlastRenderer.cs:156-166). Forced into this scratch branch by
                    // hasLayerSim ⇒ needScratch above (retained state + overwrite semantics), mirroring Fire/Fireball.
                    if (hasLayerSim) ApplyLayerSim(layer.simulationModifier, spec.seed, scratch, W, H, frameIndex, frames);
                    // Impose the active luma matte on this finished-but-uncomposited layer (its six channels, in the
                    // fixed order), BEFORE the clip/composite so the mask shapes this layer's own pixels. oneShot
                    // (NextLayer scope) consumes the matte after this one Draw layer; AllAbove persists until another
                    // LumaMatte replaces it. Isolation is forced above (matteActive ⇒ needScratch), mirroring
                    // BlastRenderer's per-layer scratch for a matte-affected layer.
                    if (matteActive)
                    {
                        ApplyMatte(scratch, matteState.mask, matteState.flags, matteState.blurAmt,
                                   matteState.dispAmt, matteState.hueDeg, matteState.alphaSource, W, H);
                        if (matteState.oneShot) matteState = default;
                    }
                    float[] clip = hasClip ? channels[layer.clipByChannel] : null;
                    CompositeLayer(buf, scratch, clip, layer.clipInvert);
                    if (borderBuf != null && layer.borderOverMatte) (deferredBorders ??= new List<Color32[]>()).Add(borderBuf);
                }
                bufDirty = true;
            }

            // ── first-class border draw-over-matte (task #65) ────────────────────────────────────────────────────
            // Composite the deferred over-matte borders ON TOP of the fully-composited stack, in paint order, BEFORE
            // the global post passes (so post effects include the borders). Each buffer is a straight-colour rim whose
            // alpha already carries band × the shape's coverage, so a plain Over draws it. null ⇒ no over-matte border
            // anywhere ⇒ skipped ⇒ byte-identical to pre-border.
            if (deferredBorders != null)
                for (int i = 0; i < deferredBorders.Count; i++)
                    CompositeLayer(buf, deferredBorders[i], null, false);

            // ── spec-wide GLOBAL post passes (task #56) ───────────────────────────────────────────────────────
            // Whole-frame post modifiers (Bloom / Outline / Kaleidoscope / …) from the spec's globalModifiers list,
            // in list order, AFTER every layer has composited — the exact stage & keying of BlastRenderer's trailing
            // global PostModifier loop (Runtime/Pyre/BlastRenderer.cs:958-971). Evaluated at the whole-timeline blast
            // `life` (Pyre1's bp), layer-independent (GlobalLayerSalt pinned so a MinMax post param doesn't inherit
            // the last layer's salt). Empty globalModifiers ⇒ this loop does nothing ⇒ byte-identical to pre-#56.
            if (spec.globalModifiers != null)
            {
                int savedSalt = _layerSalt;
                _layerSalt = GlobalLayerSalt;
                for (int i = 0; i < spec.globalModifiers.Count; i++)
                {
                    var m = spec.globalModifiers[i];
                    if (m == null || !m.enabled) continue;
                    if (m is PostModifier post)
                    {
                        SetPostContext(post, life, spec.seed, frameIndex);
                        int idx = i;
                        m.Prepare((v, fid) => Eval(v, life, spec.seed, ModParticleIndex, FldModifier + (GlobalPostBase + idx) * 8 + fid));
                        post.Apply(buf, W, H);
                    }
                }
                _layerSalt = savedSalt;
            }
            return buf;
        }

        // Dispatch one layer's particles into `target` (its own scratch or, for the first drawn layer, buf itself).
        // The swarm-off / swarm-on / Text-line split of the pre-R3 RenderFrame, made layer-relative. _layerSalt and
        // _textReady are already set for this layer by the caller.
        static void RenderLayer(Color32[] target, int W, int H, float life, PyrePlusSpec spec, PyrePlusLayer layer,
                                in ModSet mods, float phase, int frameIndex)
        {
            // Plug-in form (PlusForm.cs) — the ONE dispatch for every form that is not a built-in enum case. The
            // renderer's only jobs here are the things every form shares: resolve the dials through the Eval funnel
            // (Prepare), hand over the layer's fill/alpha/modifiers and — swarm on — the full per-particle instance
            // list the particle forms compute, then let the form paint the isolated scratch the caller forced.
            if (layer.form != null)
            {
                RenderFormLayer(target, W, H, life, spec, layer, mods, phase, frameIndex);
                return;
            }
            // Fire (slice 6a) — a STATEFUL sim form has NO particles/swarm, so it bypasses the swarm dispatch entirely
            // and drives its own replay harness. The caller ALWAYS hands it an isolated scratch (needScratch is forced
            // for Fire in RenderFrame) because FireSim.Render OVERWRITES above-threshold pixels and leaves the rest —
            // it must never render straight into the shared output buffer. The per-layer sim seed reads _layerSalt
            // (set by the caller = this layer's index), so two Fire layers never share a sim.
            if (layer.shapeForm == ShapeForm.Fire) { RenderFireLayer(target, W, H, life, spec, layer, frameIndex); return; }
            // Fireball (slice 6b) — the second stateful sim form; identical treatment to Fire (no swarm, isolated
            // scratch, own replay harness). Dispatch and return before the swarm/DrawParticle path.
            if (layer.shapeForm == ShapeForm.Fireball) { RenderFireballLayer(target, W, H, life, spec, layer, frameIndex); return; }
            // Playback3D — PROOF-OF-CONCEPT form, EDITOR-PREVIEW ONLY. There is deliberately no runtime bake here
            // yet: its whole live-3D/pixelated preview lives in PyrePlusPlayback3DPreview (a PreviewRenderUtility
            // scene driving the assigned prefab's ParticleSystem(s)), which this pure per-frame pixel renderer has
            // no access to (no Unity scene/camera at bake time, and no defined mapping from a 3D particle sim to
            // this canvas yet). A spec using this form renders NOTHING here (this layer contributes no pixels) —
            // honestly incomplete rather than faking a placeholder shape. Extending this to an actual bake (e.g.
            // rendering the prefab to an offscreen camera per frame and compositing that) is future work.
            if (layer.shapeForm == ShapeForm.Playback3D) return;

            if (!layer.swarmEnabled)
            {
                // Text + a usable font: the whole string is ONE centred line (each char its own particle), painted
                // far→near for correct occlusion. Every other form — and Text with no font — draws the single
                // centred particle.
                if (layer.shapeForm == ShapeForm.Text && _textReady)
                    RenderTextLine(target, W, H, life, spec, layer, mods, frameIndex);
                else
                    DrawParticle(target, W, H, W * 0.5f, H * 0.5f, life, spec, layer, 0, mods, phase, frameIndex);
            }
            else
                RenderSwarm(target, W, H, life, spec, layer, mods, phase, frameIndex);
        }

        static void RenderFormLayer(Color32[] target, int W, int H, float life, PyrePlusSpec spec, PyrePlusLayer layer,
                                    in ModSet mods, float phase, int frameIndex)
        {
            int sd = spec != null ? spec.seed : 0;
            int salt = _layerSalt;
            var form = layer.form;
            form.Prepare(new PlusFormPrepareCtx(life, sd, salt, (v, fid) => Eval(v, life, sd, ModParticleIndex, fid)));
            float alpha = Mathf.Clamp01(Eval(layer.alpha, life, sd, ModParticleIndex, FldAlpha));

            // Swarm hand-off: every spawn as RenderSwarm would see it this frame — position with the LIVE whole-cloud
            // spin then scale applied (final = centre + scale·spin(offset)), own life (die-together honoured), depth
            // multipliers. All instances are passed, alive or not; the form decides what an unborn/dead one means.
            PlusSwarmInstance[] swarm = null;
            if (layer.swarmEnabled)
            {
                ComputeSpawns(spec, layer, _plusFireSpawns);
                bool dieTogether = layer.swarmDieTogether;
                float deathPoint = 0f;
                if (dieTogether)
                {
                    // The same shared death point RenderSwarm derives (see its comment there).
                    int lastIdx = Mathf.Max(0, _plusFireSpawns.Count - 1);
                    float maxSpawnLife = layer.swarmTiming == SwarmTiming.FrameStep
                        ? Mathf.Clamp01((layer.swarmFirstFrame + lastIdx * layer.swarmFrameStep) / (float)Mathf.Max(1, spec.frameCount - 1))
                        : Mathf.Clamp01(EvalCanonical(layer.swarmSpawnTiming, 1f));
                    deathPoint = Mathf.Min(1f, maxSpawnLife + layer.swarmParticleLife);
                }
                // Scale-by-index folds into sizeMul exactly as RenderSwarm does (Static 1 = exact no-op).
                bool scaleIdx = !IsStaticOne(layer.swarmScaleByIndex);
                swarm = new PlusSwarmInstance[_plusFireSpawns.Count];
                for (int i = 0; i < _plusFireSpawns.Count; i++)
                {
                    var sp = _plusFireSpawns[i];
                    Vector2 wp = ApplySwarmScale(spec, layer, ApplySwarmSpin(spec, layer, sp.pos, life), life);
                    float sizeMul = Mathf.Clamp(1f + 0.35f * sp.zNorm, 0.5f, 1.6f);
                    if (scaleIdx)
                    {
                        float t = _plusFireSpawns.Count > 1 ? i / (float)(_plusFireSpawns.Count - 1) : 0f;
                        sizeMul *= Mathf.Max(0f, Eval(layer.swarmScaleByIndex, t, sd, i, FldScaleByIndex));
                    }
                    swarm[i] = new PlusSwarmInstance
                    {
                        x = wp.x, y = wp.y,
                        own = dieTogether
                            ? (life - sp.spawnLife) / Mathf.Max(0.0001f, deathPoint - sp.spawnLife)
                            : (life - sp.spawnLife) / Mathf.Max(0.0001f, layer.swarmParticleLife),
                        spawnLife = sp.spawnLife,
                        index = i,
                        orientDeg = sp.orientDeg,
                        zNorm = sp.zNorm,
                        sizeMul = sizeMul,
                        brightMul = Mathf.Clamp(1f + 0.30f * sp.zNorm, 0.55f, 1.45f),
                    };
                }
            }

            int frames = spec != null ? Mathf.Max(1, spec.frameCount) : 1;
            var ctx = new PlusFormCtx(W, H, life, sd, salt, layer.shapeFill, alpha, swarm,
                                      mods.AnyGeo ? mods.geo : null, mods.AnyPix ? mods.pix : null, phase, frameIndex,
                                      frames, (v, fid, atLife) => Eval(v, atLife, sd, ModParticleIndex, fid));
            form.Render(ctx, target);
            // Generic geometry pass (T-0058): a form that does not warp per sample gets the layer's geometry
            // modifiers applied to its finished buffer. Skipped entirely with no geometry modifiers, so the default
            // path is the untouched code above. The same integer map warps the published parity planes below.
            int[] warpMap = mods.AnyGeo && !form.HandlesGeometry ? PlusFormWarp.BuildMap(W, H, mods.geo, phase) : null;
            if (warpMap != null) PlusFormWarp.Apply(target, warpMap);
            // Parity-harness hook: only while a dump has installed a sink (null in normal operation).
            if (PlusFormDebug.FieldSink != null && form is IPlusFieldPublisher pub)
            {
                var sink = PlusFormDebug.FieldSink;
                if (warpMap != null)
                    pub.PublishFields((name, plane) => sink(name, plane != null && plane.Length == warpMap.Length ? PlusFormWarp.Apply(plane, warpMap) : plane));
                else
                    pub.PublishFields(sink);
            }
        }

        // ── Fire form: stateful sim + replay harness (slice 6a) ──────────────────────────────────────────────────
        // PyrePlus's renderer is otherwise pure-per-frame (every frame renders standalone). Fire is the ONE stateful
        // form: heat is carried by a velocity field, so frame N depends on frame N-1. It stays fully DETERMINISTIC by
        // being reached via REPLAY from a fixed reset — exactly the discipline Pyre1's RenderFire (BlastRenderer.cs:308)
        // and SimulationModifier use. We REUSE Pyre's public FireSim/FireParams directly (no re-port of the grid
        // physics); this is only the thin replay harness around them.
        //
        // One FireSim per layer, kept across frames so normal forward playback steps ONE frame instead of replaying the
        // whole history every repaint (O(frames) not O(frames²) per shown frame). A ConditionalWeakTable keyed by layer
        // IDENTITY means a deleted layer's sim is collected on its own and a cloned layer (a fresh object) gets its own
        // entry — two specs holding equal layers still get separate sims.
        //
        // INVALIDATION is CONTENT-HASH based — stronger than Pyre1's implicit same-frame-re-request replay. The hash
        // folds every input that can change the output (all fire params + arms/steps/armMode + threshold/contrast + the
        // ramp gradient + the alpha envelope + seed + frameCount + W/H). ANY authoring edit changes the hash ⇒ a forced
        // COLD replay from frame 0, so PyrePlus never forward-steps across a param change (no stale frame). Because the
        // params are provably frozen WITHIN one hash, a same-frame re-request with an UNCHANGED hash needs no step at
        // all — the grid is already at that frame; just re-render.
        //
        // CHECKPOINTS — replay only K→f instead of 0→f — are a valid further win the content-hash gate makes safe
        // (params provably frozen), but are DEFERRED this slice: correctness (replay-from-0) first.
        // TODO(slice 6+): snapshot sim state every K≈8–16 frames and replay only checkpoint→f, turning a cold scrub
        //                 from O(f) into O(K).
        sealed class FireSimEntry { public FireSim sim; public int contentHash; public int lastFrame = -1; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PyrePlusLayer, FireSimEntry> _fireSims =
            new System.Runtime.CompilerServices.ConditionalWeakTable<PyrePlusLayer, FireSimEntry>();

        // dt is ONE FRAME split across the substeps (mirrors Pyre's FireDt) — every velocity dial reads in px/frame.
        const float FireDt = 1f;

        static void RenderFireLayer(Color32[] target, int W, int H, float life, PyrePlusSpec spec,
                                    PyrePlusLayer layer, int frameIndex)
        {
            // Swarm-driven emitters (slice 8): when opted in AND the swarm is enabled, the flame sources its
            // emitters from the SWARM (one heat/fuel injection per alive particle) via a PyrePlus-local PlusFireSim
            // on its OWN replay harness — the fixed-emitter FireSim path below is left untouched. OFF or swarm off ⇒
            // the slice-6a built-in fixed-emitter path runs verbatim (byte-identical).
            if (layer.fireSwarmEmitters && layer.swarmEnabled)
            {
                RenderPlusFireLayer(target, W, H, life, spec, layer, frameIndex);
                return;
            }

            var entry = _fireSims.GetValue(layer, _ => new FireSimEntry { sim = new FireSim() });
            var sim = entry.sim;

            // A per-(spec, layer) sim seed. _layerSalt is this layer's index (set by RenderFrame before the layer
            // draws), so two Fire layers — and the same layer at two list positions — get distinct, stable seeds.
            int seed = Hash(spec != null ? spec.seed : 0, _layerSalt, 0, 0);
            int hash = FireContentHash(layer, spec, W, H, seed);

            int frames = Mathf.Max(1, spec != null ? spec.frameCount : 1);
            int last = Mathf.Clamp(frameIndex, 0, frames - 1);

            bool sizeChanged = sim.W != W || sim.H != H;
            sim.Allocate(W, H);

            // Any size / param / gradient / seed / frameCount change, or an uninitialised sim ⇒ the cache is invalid.
            bool invalid = sizeChanged || hash != entry.contentHash || entry.lastFrame < 0;
            if (!invalid && last == entry.lastFrame + 1)
            {
                // WARM forward-step: exactly one frame past where the sim is (normal playback) — O(1).
                StepFire(sim, layer, spec, seed, last);
                entry.lastFrame = last;
            }
            else if (!invalid && last == entry.lastFrame)
            {
                // Same frame re-asked with UNCHANGED content — the grid is already at `last`; just re-render below.
            }
            else
            {
                // COLD replay from frame 0 (a scrub, a backward step, a forward skip, or any invalidation). O(f).
                sim.Reset();
                for (int f = 0; f <= last; f++) StepFire(sim, layer, spec, seed, f);
                entry.lastFrame = last;
            }
            entry.contentHash = hash;

            // Composite the current grid through the layer's own fill gradient (the smoke→fire ramp) at the layer's
            // overall alpha over its life. Fire ignores `size` — the reach radius bounds it, not a particle radius.
            Gradient ramp = layer.shapeFill != null ? layer.shapeFill.gradient : null;
            float alpha = Mathf.Clamp01(Eval(layer.alpha, life, spec != null ? spec.seed : 0, ModParticleIndex, FldAlpha));
            // Off-centre emitter (task #61): the offset only shifts WHERE the finished flame lands, never the sim — the
            // grid was stepped centred (byte-faithful physics) and the offset does not touch it, so a changed offset
            // needs NO cache invalidation (sim.Render runs every repaint regardless of the warm/cold/same-frame branch
            // above). DEFAULT (0,0) takes the EXACT pre-change path — render straight into `target` — so it stays
            // byte-identical; a non-zero offset renders into a scratch and blits it shifted by integer px (Y up).
            int offX = Mathf.RoundToInt(layer.fireEmitterOffset.x);
            int offY = Mathf.RoundToInt(layer.fireEmitterOffset.y);
            if (offX == 0 && offY == 0)
                sim.Render(target, ramp, alpha, layer.fireThreshold, layer.fireContrast);
            else
            {
                var tmp = new Color32[W * H];
                sim.Render(tmp, ramp, alpha, layer.fireThreshold, layer.fireContrast);
                BlitOffset(tmp, target, W, H, offX, offY);
            }
        }

        // Copy the lit pixels of `src` into `dst` shifted by (dx, dy) integer px (dy > 0 = higher grid y = UP). Only src
        // pixels with alpha > 0 are copied, and only where they land in bounds; `dst` is a fresh (transparent) scratch,
        // so this preserves FireSim.Render's "write the lit pixels, leave the rest" semantics — just at a translated
        // position. Used by RenderFireLayer for a non-zero fireEmitterOffset (the offset == 0 case never calls this, so
        // the default flame never pays for the extra buffer/copy).
        static void BlitOffset(Color32[] src, Color32[] dst, int W, int H, int dx, int dy)
        {
            for (int y = 0; y < H; y++)
            {
                int ty = y + dy;
                if (ty < 0 || ty >= H) continue;
                int rowSrc = y * W, rowDst = ty * W;
                for (int x = 0; x < W; x++)
                {
                    if (src[rowSrc + x].a == 0) continue;
                    int tx = x + dx;
                    if (tx < 0 || tx >= W) continue;
                    dst[rowDst + tx] = src[rowSrc + x];
                }
            }
        }

        // One frame's Fire step, replayed identically every time frame f is reached. lp = the layer's life progress at
        // f (startFrame is always 0 in PyrePlus, endFrame = frameCount-1), so this mirrors Pyre's StepFire exactly with
        // startFrame folded to 0. fireSteps substeps split ONE frame of dt; the tiny per-substep phase nudge matches
        // Pyre (BlastRenderer.cs:299) so the noise advances smoothly within a frame.
        static void StepFire(FireSim sim, PyrePlusLayer layer, PyrePlusSpec spec, int seed, int f)
        {
            int frames = Mathf.Max(1, spec != null ? spec.frameCount : 1);
            float lp = Mathf.Clamp01(f / (float)Mathf.Max(1, frames - 1));
            var p = FireParamsAt(layer, spec, lp);
            int steps = Mathf.Max(1, layer.fireSteps);
            for (int s = 0; s < steps; s++)
                sim.Step(p, seed, lp + s / (float)steps * 0.01f, FireDt / steps);
        }

        // Evaluate the Fire dials at a life progress into a FireParams — a faithful mirror of Pyre's FireParamsAt
        // (BlastRenderer.cs:258), through PyrePlus's own Eval funnel (frame-global at ModParticleIndex). intensity
        // scales the injected heat + fuel so a single envelope shapes ignite→roar→die.
        static FireParams FireParamsAt(PyrePlusLayer layer, PyrePlusSpec spec, float lp)
        {
            int seed = spec != null ? spec.seed : 0;
            float E(ZUIValue v, int fid) => Eval(v, lp, seed, ModParticleIndex, fid);
            float intensity = Mathf.Clamp01(E(layer.fireIntensity, F_FireIntensity));
            return new FireParams
            {
                arms = Mathf.Max(1, layer.fireArms),
                armMode = layer.fireArmMode,
                steps = Mathf.Max(1, layer.fireSteps),
                directionDeg = E(layer.fireDirection, F_FireDir),
                emitterWidth = Mathf.Max(1f, E(layer.fireEmitterWidth, F_FireWidth)),
                emitterInset = E(layer.fireEmitterInset, F_FireInset),
                heat = Mathf.Clamp01(E(layer.fireHeat, F_FireHeat) * intensity),
                fuel = Mathf.Clamp01(E(layer.fireFuel, F_FireFuel) * intensity),
                pulse = Mathf.Max(0f, E(layer.firePulse, F_FirePulse)),
                flow = E(layer.fireFlow, F_FireFlow),
                buoyancy = E(layer.fireBuoyancy, F_FireBuoy),
                curl = Mathf.Max(0f, E(layer.fireCurl, F_FireCurl)),
                curlScale = Mathf.Max(2f, E(layer.fireCurlScale, F_FireCurlScale)),
                flicker = Mathf.Max(0f, E(layer.fireFlicker, F_FireFlicker)),
                stretch = Mathf.Max(0f, E(layer.fireStretch, F_FireStretch)),
                pinch = Mathf.Max(0f, E(layer.firePinch, F_FirePinch)),
                breakup = Mathf.Max(0f, E(layer.fireBreakup, F_FireBreakup)),
                dissipation = Mathf.Max(0f, E(layer.fireDissipation, F_FireDissip)),
                burn = Mathf.Max(0f, E(layer.fireBurn, F_FireBurn)),
                reach = Mathf.Clamp01(E(layer.fireReach, F_FireReach)),
                edgeCooling = Mathf.Clamp01(E(layer.fireEdgeCooling, F_FireEdgeCool)),
            };
        }

        // A stable hash of every input that can change a Fire layer's OUTPUT — the replay cache's invalidation key.
        // Folds all fire params (each ZUIValue's full authored content), arms/armMode/steps, threshold/contrast, the
        // ramp gradient + the alpha envelope (render inputs), and seed/frameCount/W/H. ANY authoring edit flips it,
        // forcing a cold replay so a forward-step can never carry stale dial values into a later frame.
        static int FireContentHash(PyrePlusLayer L, PyrePlusSpec spec, int W, int H, int seed)
        {
            unchecked
            {
                int h = (int)2166136261u;
                h = MixI(h, seed); h = MixI(h, W); h = MixI(h, H);
                h = MixI(h, Mathf.Max(1, spec != null ? spec.frameCount : 1));
                h = MixI(h, Mathf.Max(1, L.fireArms)); h = MixI(h, (int)L.fireArmMode); h = MixI(h, Mathf.Max(1, L.fireSteps));
                h = MixI(h, L.fireThreshold.GetHashCode()); h = MixI(h, L.fireContrast.GetHashCode());
                h = MixV(h, L.fireIntensity);
                h = MixV(h, L.fireDirection); h = MixV(h, L.fireEmitterWidth); h = MixV(h, L.fireEmitterInset);
                h = MixV(h, L.fireHeat); h = MixV(h, L.fireFuel); h = MixV(h, L.firePulse);
                h = MixV(h, L.fireFlow); h = MixV(h, L.fireBuoyancy); h = MixV(h, L.fireCurl);
                h = MixV(h, L.fireCurlScale); h = MixV(h, L.fireFlicker); h = MixV(h, L.fireStretch);
                h = MixV(h, L.firePinch); h = MixV(h, L.fireBreakup); h = MixV(h, L.fireDissipation);
                h = MixV(h, L.fireBurn); h = MixV(h, L.fireReach); h = MixV(h, L.fireEdgeCooling);
                h = MixV(h, L.alpha);                                            // the render-alpha envelope (layerAlpha)
                h = MixG(h, L.shapeFill != null ? L.shapeFill.gradient : null);   // the ramp
                return h;
            }
        }

        // ── Swarm-driven Fire emitters: PlusFireSim + replay harness (slice 8) ────────────────────────────────────
        // A PARALLEL Fire path, active only when layer.fireSwarmEmitters && layer.swarmEnabled. It mirrors the fixed-
        // emitter Fire harness above EXACTLY (a per-layer sim kept across frames; content-hash invalidation; warm
        // forward-step / same-frame re-render / cold replay-from-0), but drives a PyrePlus-LOCAL PlusFireSim whose
        // Step takes a CALLER-SUPPLIED emitter list. Each frame's emitter list is (re)built deterministically from
        // ComputeSpawns — one HeatEmitter per ALIVE swarm particle, positioned at the particle and carrying the Fire
        // envelopes at that particle's OWN life — so a cold replay recomputes the identical emitters at each step and
        // the sim scrubs/bakes identically. Its OWN CWT (a fresh PlusFireSim object never shares the FireSim entry),
        // so toggling fireSwarmEmitters never disturbs the proven 6a path. Checkpoints deferred (correctness first),
        // as for the fixed path.
        sealed class PlusFireSimEntry { public PlusFireSim sim; public int contentHash; public int lastFrame = -1; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PyrePlusLayer, PlusFireSimEntry> _plusFireSims =
            new System.Runtime.CompilerServices.ConditionalWeakTable<PyrePlusLayer, PlusFireSimEntry>();

        // Per-step scratch (single-threaded editor/bake thread, same static-scratch idiom as _layerSalt). Reused so a
        // cold replay doesn't allocate a fresh emitter/spawn list per frame.
        static readonly List<HeatEmitter> _plusFireEmitters = new List<HeatEmitter>(64);
        static readonly List<SpawnPoint> _plusFireSpawns = new List<SpawnPoint>(64);

        static void RenderPlusFireLayer(Color32[] target, int W, int H, float life, PyrePlusSpec spec,
                                        PyrePlusLayer layer, int frameIndex)
        {
            var entry = _plusFireSims.GetValue(layer, _ => new PlusFireSimEntry { sim = new PlusFireSim() });
            var sim = entry.sim;

            // A per-(spec, layer) sim seed, exactly as the fixed Fire path — _layerSalt is this layer's index (set by
            // RenderFrame before the layer draws), so two swarm-Fire layers get distinct, stable seeds.
            int seed = Hash(spec != null ? spec.seed : 0, _layerSalt, 0, 0);
            int hash = PlusFireContentHash(layer, spec, W, H, seed);

            int frames = Mathf.Max(1, spec != null ? spec.frameCount : 1);
            int last = Mathf.Clamp(frameIndex, 0, frames - 1);

            bool sizeChanged = sim.W != W || sim.H != H;
            sim.Allocate(W, H);

            bool invalid = sizeChanged || hash != entry.contentHash || entry.lastFrame < 0;
            if (!invalid && last == entry.lastFrame + 1)
            {
                StepPlusFire(sim, layer, spec, seed, last, frames);   // WARM forward-step — O(1)
                entry.lastFrame = last;
            }
            else if (!invalid && last == entry.lastFrame)
            {
                // Same frame re-asked with UNCHANGED content — the grid is already at `last`; just re-render below.
            }
            else
            {
                sim.Reset();                                          // COLD replay from frame 0 — O(f)
                for (int f = 0; f <= last; f++) StepPlusFire(sim, layer, spec, seed, f, frames);
                entry.lastFrame = last;
            }
            entry.contentHash = hash;

            // Composite the grid through the layer's own fill gradient at its overall alpha over life (Fire ignores
            // `size` — the reach radius bounds it). Identical output stage to the fixed path.
            Gradient ramp = layer.shapeFill != null ? layer.shapeFill.gradient : null;
            float alpha = Mathf.Clamp01(Eval(layer.alpha, life, spec != null ? spec.seed : 0, ModParticleIndex, FldAlpha));
            sim.Render(target, ramp, alpha, layer.fireThreshold, layer.fireContrast);
        }

        // One frame's swarm-Fire step, replayed identically every time frame f is reached. lp = the layer's life at f;
        // the physics dials come from FireParamsAt (byte-faithful to the fixed path — p.heat/p.fuel/emitterWidth are
        // ignored by PlusFireSim, which reads each emitter's own heat/fuel/radius), and the emitter set is rebuilt
        // from the swarm at f. fireSteps substeps split ONE frame of dt, with the same per-substep phase nudge.
        static void StepPlusFire(PlusFireSim sim, PyrePlusLayer layer, PyrePlusSpec spec, int seed, int f, int frames)
        {
            float lp = Mathf.Clamp01(f / (float)Mathf.Max(1, frames - 1));
            var p = FireParamsAt(layer, spec, lp);
            BuildFireEmitters(spec, layer, seed, lp, frames, _plusFireEmitters);
            int steps = Mathf.Max(1, layer.fireSteps);
            for (int s = 0; s < steps; s++)
                sim.Step(p, _plusFireEmitters, seed, lp + s / (float)steps * 0.01f, FireDt / steps);
        }

        // Build the emitter list for the swarm-Fire path at layer-life `lp`: one HeatEmitter per ALIVE swarm particle.
        // Mirrors RenderSwarm's own-life / die-together / live whole-cloud rotation+scale so an emitter sits exactly
        // where the particle would be drawn; each emitter's radius/heat/fuel are the Fire envelopes at that particle's
        // OWN life (emitterWidth→radius, fireHeat×intensity→heat, fireFuel×intensity→fuel), and its pulse phaseSeed is
        // the particle index folded with the sim seed (so each source breathes on its own phase, like FireSim's Vary
        // arms). Pure/deterministic (only seeded ComputeSpawns placements + seeded envelope Evals), so replay is exact.
        static void BuildFireEmitters(PyrePlusSpec spec, PyrePlusLayer layer, int seed, float lp, int frames,
                                      List<HeatEmitter> into)
        {
            into.Clear();
            if (spec == null || layer == null) return;
            ComputeSpawns(spec, layer, _plusFireSpawns);   // frame-independent spawn positions + spawnLife
            if (_plusFireSpawns.Count == 0) return;

            float cx = spec.Width * 0.5f, cy = spec.Height * 0.5f;
            // Live whole-cloud rotation + scale at THIS frame's life (mirrors RenderSwarm's swarmRot/swarmScl).
            float sTurn = Eval(layer.swarmTurn, lp, spec.seed, ModParticleIndex, FldSwarmTurn);
            float sTilt = Eval(layer.swarmTilt, lp, spec.seed, ModParticleIndex, FldSwarmTilt);
            float sRoll = Eval(layer.swarmRoll, lp, spec.seed, ModParticleIndex, FldSwarmRoll);
            bool swarmRot = sTurn != 0f || sTilt != 0f || sRoll != 0f;
            float sScale = Eval(layer.swarmScale, lp, spec.seed, ModParticleIndex, FldSwarmScale);
            bool swarmScl = sScale != 1f;

            // Die-together shared death point (mirrors RenderSwarm).
            bool dieTogether = layer.swarmDieTogether;
            float deathPoint = 0f;
            if (dieTogether)
            {
                int lastIdx = Mathf.Max(0, _plusFireSpawns.Count - 1);
                float maxSpawnLife = layer.swarmTiming == SwarmTiming.FrameStep
                    ? Mathf.Clamp01((layer.swarmFirstFrame + lastIdx * layer.swarmFrameStep) / (float)Mathf.Max(1, spec.frameCount - 1))
                    : Mathf.Clamp01(EvalCanonical(layer.swarmSpawnTiming, 1f));
                deathPoint = Mathf.Min(1f, maxSpawnLife + layer.swarmParticleLife);
            }

            for (int i = 0; i < _plusFireSpawns.Count; i++)
            {
                var sp = _plusFireSpawns[i];
                float own = dieTogether
                    ? (lp - sp.spawnLife) / Mathf.Max(0.0001f, deathPoint - sp.spawnLife)
                    : (lp - sp.spawnLife) / Mathf.Max(0.0001f, layer.swarmParticleLife);
                if (own < 0f || own > 1f) continue;   // not alive yet / already dead this frame

                Vector2 dp = sp.pos;
                if (swarmRot)
                    dp = ApplyShapeTransform(sp.pos, cx, cy, Vector2.Distance(sp.pos, new Vector2(cx, cy)),
                                             sRoll, sTurn, sTilt, 0f, 0f, out _);
                if (swarmScl)
                    dp = new Vector2(cx + (dp.x - cx) * sScale, cy + (dp.y - cy) * sScale);

                float intensity = Mathf.Clamp01(Eval(layer.fireIntensity, own, spec.seed, i, F_FireIntensity));
                float width = Mathf.Max(1f, Eval(layer.fireEmitterWidth, own, spec.seed, i, F_FireWidth));
                float eHeat = Mathf.Clamp01(Eval(layer.fireHeat, own, spec.seed, i, F_FireHeat) * intensity);
                float eFuel = Mathf.Clamp01(Eval(layer.fireFuel, own, spec.seed, i, F_FireFuel) * intensity);
                into.Add(new HeatEmitter
                {
                    x = dp.x, y = dp.y,
                    radius = Mathf.Max(0.5f, width * 0.5f),   // Pyre's emitterWidth is a full width → half = radius
                    heat = eHeat, fuel = eFuel,
                    phaseSeed = seed + i * 7919,              // per-particle pulse phase (FireSim's Vary idiom)
                });
            }
        }

        // The swarm-Fire replay cache's invalidation key. Everything FireContentHash folds (all fire params + arms/
        // steps + threshold/contrast + ramp + alpha + seed/frames/W/H) PLUS the swarm placement config that shapes the
        // emitter set (so editing the swarm forces a cold replay too). ANY authoring edit flips it ⇒ no stale forward-
        // step. Uses only float-bit / enum-int folds (no string.GetHashCode) so it is stable across runs.
        static int PlusFireContentHash(PyrePlusLayer L, PyrePlusSpec spec, int W, int H, int seed)
        {
            unchecked
            {
                int h = FireContentHash(L, spec, W, H, seed);
                h = MixI(h, L.fireSwarmEmitters ? 1 : 0);
                // ComputeSpawns inputs (placement) + own-life + live whole-cloud transform.
                h = MixI(h, L.swarmEnabled ? 1 : 0);
                h = MixI(h, L.swarmCount);
                h = MixI(h, (int)L.swarmSpawnMode);
                h = MixI(h, (int)L.swarmShapeKind);
                h = MixI(h, (int)L.swarmTiming);
                h = MixI(h, L.swarmFirstFrame);
                h = MixI(h, L.swarmFrameStep);
                h = MixI(h, L.swarmParticleLife.GetHashCode());
                h = MixI(h, (int)L.swarmOrient);
                h = MixI(h, L.swarmEvenPath ? 1 : 0);
                h = MixI(h, L.swarmPathSpread.GetHashCode());
                h = MixI(h, L.swarmDieTogether ? 1 : 0);
                h = MixI(h, L.shapeScaleSnap.GetHashCode());
                h = MixI(h, (int)L.shapeForm);
                h = MixI(h, (L.textString ?? "").Length);   // Text overrides the swarm count with the string length
                h = MixV(h, L.swarmCustomX); h = MixV(h, L.swarmCustomY);
                h = MixV(h, L.swarmProgress); h = MixV(h, L.swarmSpawnTiming);
                h = MixV(h, L.shapeScale); h = MixV(h, L.shapeRotation);
                h = MixV(h, L.shapeYaw); h = MixV(h, L.shapePitch);
                h = MixV(h, L.shapeOffsetX); h = MixV(h, L.shapeOffsetY);
                h = MixV(h, L.swarmTurn); h = MixV(h, L.swarmTilt);
                h = MixV(h, L.swarmRoll); h = MixV(h, L.swarmScale);
                return h;
            }
        }

        // ── Fireball form: stateful cellular sim + replay harness (slice 6b) ──────────────────────────────────────
        // The SECOND stateful PyrePlus form (mirrors the Fire harness above exactly). Fireball is Pyre's cheap
        // "doom-fire" cellular flame: heat propagates OUTWARD from one central point, folded into `fireballArms`
        // kaleidoscope wedges → a radial/star explosion. It retains a frame-to-frame heat grid, so frame N depends on
        // frame N-1; it stays fully DETERMINISTIC by being reached via REPLAY from a fixed reset — the same discipline
        // as Fire and Pyre1's RenderFireball (BlastRenderer.cs:210). We REUSE Pyre's public FireballSim/FireballParams
        // directly (no re-port of the cellular physics); this is only the thin replay harness around them.
        //
        // The harness is DUPLICATED from Fire (not generalised): FireSim and FireballSim are unrelated types with
        // different Step signatures — FireSim.Step takes (params, seed, t, dt) with fireSteps substeps, FireballSim.Step
        // takes (params, seed, t, INTEGER frameIndex) with none — so a shared generic entry would buy nothing and the
        // duplication keeps the proven Fire path byte-untouched.
        //
        // INVALIDATION is CONTENT-HASH based, identical to Fire: the hash folds every fireball input (all six rate
        // envelopes + arms/mirror + threshold/contrast + the ramp gradient + the alpha envelope + seed + frameCount +
        // W/H). ANY authoring edit flips it ⇒ a forced COLD replay from frame 0, so a forward-step never carries stale
        // dial values into a later frame. Checkpoints are DEFERRED this slice, as for Fire (correctness first).
        sealed class FireballSimEntry { public FireballSim sim; public int contentHash; public int lastFrame = -1; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PyrePlusLayer, FireballSimEntry> _fireballSims =
            new System.Runtime.CompilerServices.ConditionalWeakTable<PyrePlusLayer, FireballSimEntry>();

        static void RenderFireballLayer(Color32[] target, int W, int H, float life, PyrePlusSpec spec,
                                        PyrePlusLayer layer, int frameIndex)
        {
            var entry = _fireballSims.GetValue(layer, _ => new FireballSimEntry { sim = new FireballSim() });
            var sim = entry.sim;

            // A per-(spec, layer) sim seed, exactly as Fire — _layerSalt is this layer's index (set by RenderFrame
            // before the layer draws), so two Fireball layers (or the same layer at two list positions) get distinct,
            // stable seeds.
            int seed = Hash(spec != null ? spec.seed : 0, _layerSalt, 0, 0);
            int hash = FireballContentHash(layer, spec, W, H, seed);

            int frames = Mathf.Max(1, spec != null ? spec.frameCount : 1);
            int last = Mathf.Clamp(frameIndex, 0, frames - 1);

            bool sizeChanged = sim.W != W || sim.H != H;
            sim.Allocate(W, H);

            // Any size / param / gradient / seed / frameCount change, or an uninitialised sim ⇒ the cache is invalid.
            bool invalid = sizeChanged || hash != entry.contentHash || entry.lastFrame < 0;
            if (!invalid && last == entry.lastFrame + 1)
            {
                // WARM forward-step: exactly one frame past where the sim is (normal playback) — O(1).
                StepFireball(sim, layer, spec, seed, last);
                entry.lastFrame = last;
            }
            else if (!invalid && last == entry.lastFrame)
            {
                // Same frame re-asked with UNCHANGED content — the grid is already at `last`; just re-render below.
            }
            else
            {
                // COLD replay from frame 0 (a scrub, a backward step, a forward skip, or any invalidation). O(f).
                sim.Reset();
                for (int f = 0; f <= last; f++) StepFireball(sim, layer, spec, seed, f);
                entry.lastFrame = last;
            }
            entry.contentHash = hash;

            // Composite the current grid through the layer's own fill gradient (the smoke→fire ramp) at the layer's
            // overall alpha over its life. Fireball ignores `size` — the reach radius bounds it, not a particle radius.
            Gradient ramp = layer.shapeFill != null ? layer.shapeFill.gradient : null;
            float alpha = Mathf.Clamp01(Eval(layer.alpha, life, spec != null ? spec.seed : 0, ModParticleIndex, FldAlpha));
            sim.Render(target, ramp, alpha, layer.fireballThreshold, layer.fireballContrast);
        }

        // One frame's Fireball step, replayed identically every time frame f is reached. lp = the layer's life progress
        // at f (startFrame folds to 0, endFrame = frameCount-1). Unlike Fire there are NO substeps — FireballSim.Step
        // takes the INTEGER frame index `f` as its random key (Pyre passes the same, BlastRenderer.cs:248), so a cold
        // scrub to a frame reproduces that frame's exact randomness. `lp` is only the per-cell flicker phase.
        static void StepFireball(FireballSim sim, PyrePlusLayer layer, PyrePlusSpec spec, int seed, int f)
        {
            int frames = Mathf.Max(1, spec != null ? spec.frameCount : 1);
            float lp = Mathf.Clamp01(f / (float)Mathf.Max(1, frames - 1));
            var p = FireballParamsAt(layer, spec, lp);
            sim.Step(p, seed, lp, f);
        }

        // Evaluate the Fireball dials at a life progress into a FireballParams — a faithful mirror of Pyre's StepFireball
        // param build (BlastRenderer.cs:237), through PyrePlus's own Eval funnel (frame-global at ModParticleIndex).
        static FireballParams FireballParamsAt(PyrePlusLayer layer, PyrePlusSpec spec, float lp)
        {
            int seed = spec != null ? spec.seed : 0;
            float E(ZUIValue v, int fid) => Eval(v, lp, seed, ModParticleIndex, fid);
            return new FireballParams
            {
                sourceHeat = Mathf.Clamp01(E(layer.fireballSource, F_FireballSource)),
                sourceRadius = Mathf.Max(1f, E(layer.fireballSourceRadius, F_FireballRadius)),
                cooling = Mathf.Max(0.001f, E(layer.fireballCooling, F_FireballCool)),
                spread = Mathf.Max(0f, E(layer.fireballSpread, F_FireballSpread)),
                reach = Mathf.Clamp01(E(layer.fireballReach, F_FireballReach)),
                sharpness = Mathf.Max(0f, E(layer.fireballSharpness, F_FireballSharp)),
                arms = Mathf.Max(1, layer.fireballArms),
                mirror = layer.fireballMirror,
            };
        }

        // A stable hash of every input that can change a Fireball layer's OUTPUT — the replay cache's invalidation key
        // (mirrors FireContentHash). Folds the six rate envelopes, arms/mirror, threshold/contrast, the ramp gradient +
        // the alpha envelope, and seed/frameCount/W/H. ANY authoring edit flips it, forcing a cold replay.
        static int FireballContentHash(PyrePlusLayer L, PyrePlusSpec spec, int W, int H, int seed)
        {
            unchecked
            {
                int h = (int)2166136261u;
                h = MixI(h, seed); h = MixI(h, W); h = MixI(h, H);
                h = MixI(h, Mathf.Max(1, spec != null ? spec.frameCount : 1));
                h = MixI(h, Mathf.Max(1, L.fireballArms)); h = MixI(h, L.fireballMirror ? 1 : 0);
                h = MixI(h, L.fireballThreshold.GetHashCode()); h = MixI(h, L.fireballContrast.GetHashCode());
                h = MixV(h, L.fireballSource); h = MixV(h, L.fireballSourceRadius); h = MixV(h, L.fireballCooling);
                h = MixV(h, L.fireballSharpness); h = MixV(h, L.fireballSpread); h = MixV(h, L.fireballReach);
                h = MixV(h, L.alpha);                                            // the render-alpha envelope (layerAlpha)
                h = MixG(h, L.shapeFill != null ? L.shapeFill.gradient : null);   // the ramp
                return h;
            }
        }

        static int MixI(int h, int x) { unchecked { return (h ^ x) * 16777619; } }

        static int MixV(int h, ZUIValue v)
        {
            unchecked
            {
                if (v == null) return MixI(h, 0);
                h = MixI(h, (int)v.mode);
                h = MixI(h, v.staticValue.GetHashCode());
                h = MixI(h, v.min.GetHashCode());
                h = MixI(h, v.max.GetHashCode());
                h = MixI(h, v.yMax.GetHashCode());
                var pts = v.points;
                h = MixI(h, pts != null ? pts.Count : 0);
                if (pts != null)
                    for (int i = 0; i < pts.Count; i++)
                    {
                        var p = pts[i];
                        h = MixI(h, p.time.GetHashCode());
                        h = MixI(h, p.value.GetHashCode());
                        h = MixI(h, p.exponent.GetHashCode());
                    }
                return h;
            }
        }

        static int MixG(int h, Gradient g)
        {
            unchecked
            {
                if (g == null) return MixI(h, 0);
                var ck = g.colorKeys; var ak = g.alphaKeys;
                h = MixI(h, ck.Length); h = MixI(h, ak.Length);
                for (int i = 0; i < ck.Length; i++) { h = MixI(h, ck[i].color.GetHashCode()); h = MixI(h, ck[i].time.GetHashCode()); }
                for (int i = 0; i < ak.Length; i++) { h = MixI(h, ak[i].alpha.GetHashCode()); h = MixI(h, ak[i].time.GetHashCode()); }
                h = MixI(h, (int)g.mode);
                return h;
            }
        }

        // True when a modifier list holds an enabled PostModifier — the layer then needs an isolated buffer so its
        // post pass shapes only its own pixels (mirrors BlastRenderer's hasLayerPost test).
        static bool HasEnabledPost(List<PyreModifier> list)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++) { var m = list[i]; if (m != null && m.enabled && m is PostModifier) return true; }
            return false;
        }

        // Fold a WriteMatte layer's contribution into a mask channel. Max = the union of masks (the default, the
        // design's max-combine); Add/Subtract accumulate/carve and clamp to 0..1. Each pixel contributes either its
        // COVERAGE (its alpha — the classic numbered-channel path) or, when useLuma (slice 4b), its LUMINANCE × alpha
        // — so several such layers on one channel fuse into ONE scalar HEIGHTMAP a Draw layer renders relief-lit.
        // useLuma == false is byte-identical to the pre-slice-4b coverage-only path.
        static void WriteMatteCoverage(float[] channel, Color32[] scratch, MatteCombine combine, bool useLuma)
        {
            for (int i = 0; i < channel.Length; i++)
            {
                float cov = useLuma ? Luma(scratch[i]) * (scratch[i].a * (1f / 255f)) : scratch[i].a * (1f / 255f);
                switch (combine)
                {
                    case MatteCombine.Add:      channel[i] = Mathf.Clamp01(channel[i] + cov); break;
                    case MatteCombine.Subtract: channel[i] = Mathf.Clamp01(channel[i] - cov); break;
                    default:                    channel[i] = Mathf.Max(channel[i], cov); break;
                }
            }
        }

        // Composite an isolated Draw layer's scratch onto the frame, multiplying each pixel's alpha by its clip
        // channel first (invert ⇒ 1 - channel). clip == null ⇒ a plain Over composite.
        static void CompositeLayer(Color32[] buf, Color32[] scratch, float[] clip, bool invert)
        {
            for (int i = 0; i < buf.Length; i++)
            {
                var s = scratch[i];
                float a = s.a * (1f / 255f);
                if (clip != null)
                {
                    float k = invert ? 1f - clip[i] : clip[i];
                    a *= Mathf.Clamp01(k);
                }
                if (a <= 0.0001f) continue;
                Over(buf, i, s.r * (1f / 255f), s.g * (1f / 255f), s.b * (1f / 255f), a);
            }
        }

        // ── first-class border (task #60 / #65) ──────────────────────────────────────────────────────────────────
        // The six FLAT 2D forms get an optional coloured RIM. It is drawn as a per-layer SILHOUETTE-OUTLINE POST-PASS
        // over the layer's finished alpha — ONE general implementation for all six, rather than threading a border
        // band through each form's own boundary math (Disc's d-vs-radius, the Star/Polygon per-ray bound, the
        // Crescent's two rims, the Streak capsule, the Ring annulus): the pass reads the rendered coverage and rims
        // whatever silhouette the layer drew (a single shape, or the union of a whole swarm). Chosen because it (a)
        // works identically on all six forms with no per-form edits, and (b) keeps the FILL and the BORDER as
        // separate buffers, which is exactly what the draw-over-matte split (#65) needs — the fill can feed a matte
        // while the border composites separately. Gated to the flat-2D set so the 3D solids (their own edge lines),
        // Text (its own border) and Sprite/Fire/Fireball/Sparkle are never rimmed.
        static bool IsFlat2DBorderForm(ShapeForm f) =>
            f == ShapeForm.Disc || f == ShapeForm.Crescent || f == ShapeForm.Ring ||
            f == ShapeForm.Streak || f == ShapeForm.Star || f == ShapeForm.Polygon;

        // Inside-distance transform: for every pixel, its (approximate Euclidean) distance to the nearest FULLY-
        // TRANSPARENT pixel (fill.a == 0). A two-pass chamfer (ortho 1, diagonal √2) — background pixels seed 0 and
        // the distance grows inward, so an interior pixel's value is how far it sits from the silhouette edge. Cheap
        // (two linear sweeps) and exact enough at these canvas sizes; the border band reads it directly.
        static float[] BorderInsideDistance(Color32[] fill, int W, int H)
        {
            const float BIG = 1e9f, A = 1f, B = 1.41421356f;
            var d = new float[W * H];
            for (int i = 0; i < d.Length; i++) d[i] = fill[i].a == 0 ? 0f : BIG;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x; float v = d[i];
                    if (v == 0f) continue;
                    if (x > 0) v = Mathf.Min(v, d[i - 1] + A);
                    if (y > 0) v = Mathf.Min(v, d[i - W] + A);
                    if (x > 0 && y > 0) v = Mathf.Min(v, d[i - W - 1] + B);
                    if (x < W - 1 && y > 0) v = Mathf.Min(v, d[i - W + 1] + B);
                    d[i] = v;
                }
            for (int y = H - 1; y >= 0; y--)
                for (int x = W - 1; x >= 0; x--)
                {
                    int i = y * W + x; float v = d[i];
                    if (v == 0f) continue;
                    if (x < W - 1) v = Mathf.Min(v, d[i + 1] + A);
                    if (y < H - 1) v = Mathf.Min(v, d[i + W] + A);
                    if (x < W - 1 && y < H - 1) v = Mathf.Min(v, d[i + W + 1] + B);
                    if (x > 0 && y < H - 1) v = Mathf.Min(v, d[i + W - 1] + B);
                    d[i] = v;
                }
            return d;
        }

        // Build the border layer for a flat-2D form: a TRANSPARENT buffer whose only non-zero pixels are the outermost
        // `borderWidth` px of the silhouette, painted in the border fill. The rim alpha = the fill's own alpha × a band
        // weight (1 across the band, feathering to 0 one px past the width for a clean inner edge) × the shape's OWN
        // coverage at that pixel — so the rim inherits the shape's anti-aliased edge instead of hard-cutting it, and
        // never extends past the silhouette. Returns null when the border is a no-op (width ≤ 0, or the shape drew
        // nothing). The caller Over-composites it onto the fill (border ON the layer) or defers it (borderOverMatte).
        // `life` is the layer's own life clock. A spatial border fill is mapped across the silhouette's bounding box.
        static Color32[] BuildBorderBuffer(Color32[] fill, int W, int H, float life, PyrePlusSpec spec, PyrePlusLayer layer)
        {
            float width = Eval(layer.borderWidth, life, spec.seed, ModParticleIndex, FldBorderWidth);
            if (width <= 0.01f) return null;

            var dist = BorderInsideDistance(fill, W, H);

            var bfill = layer.borderFill;
            bool spatial = IsSpatialFill(bfill);
            Color flat = bfill != null ? bfill.Evaluate(life, 0f, 0f) : Color.white;

            // Bounding box of the silhouette (only needed to map a spatial border fill's (u,v) across the shape).
            int minX = W, minY = H, maxX = -1, maxY = -1;
            if (spatial)
            {
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                        if (fill[y * W + x].a != 0)
                        { if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }
                if (maxX < minX) return null;   // no coverage at all
            }
            float invW = spatial && maxX > minX ? 2f / (maxX - minX) : 0f;
            float invH = spatial && maxY > minY ? 2f / (maxY - minY) : 0f;

            var outBuf = new Color32[W * H];   // transparent-cleared
            bool any = false;
            for (int i = 0; i < outBuf.Length; i++)
            {
                byte fa = fill[i].a;
                if (fa == 0) continue;                          // outside the silhouette — never a rim pixel
                float bw = Mathf.Clamp01(width + 1f - dist[i]); // 1 across the outer `width` px, feathering 1 px past it
                if (bw <= 0f) continue;                          // deeper than the band — plain fill, no rim here
                Color bc = flat;
                if (spatial)
                {
                    int x = i % W, y = i / W;
                    float u = (x - minX) * invW - 1f;
                    float v = (y - minY) * invH - 1f;
                    bc = EvalFill(bfill, life, u, v, x, y, W, H);
                }
                float a = bc.a * bw * (fa * (1f / 255f));        // rim = border alpha × band × the shape's own coverage
                if (a <= 0.002f) continue;
                outBuf[i] = new Color32(
                    (byte)(Mathf.Clamp01(bc.r) * 255f),
                    (byte)(Mathf.Clamp01(bc.g) * 255f),
                    (byte)(Mathf.Clamp01(bc.b) * 255f),
                    (byte)(Mathf.Clamp01(a) * 255f));
                any = true;
            }
            return any ? outBuf : null;
        }

        // ── luma matte (slice 4) ───────────────────────────────────────────────────────────────────────────────
        // Pyre1's six-channel luminance matte, ported VERBATIM from BlastRenderer (Runtime/Pyre/BlastRenderer.cs:
        // 349-488). All are dependency-free Color32[]/float[] ops, so they cross the asmdef boundary cleanly. A
        // LumaMatte-role layer's finished pixels become a MASK (luminance × the layer's own alpha, optionally
        // inverted, × strength); each subsequent Draw layer runs ApplyMatte with that mask before it composites.
        // This is a NEW, PARALLEL capability — the WriteMatte numbered-channel coverage-clip path is untouched.

        // The active luma matte carried BY-REF through the layer loop. mask == null ⇒ no matte active.
        struct MatteState
        {
            public float[] mask;        // luminance×alpha mask of the LumaMatte layer, sized W*H; null = inactive
            public MatteChannel flags;  // which channels the matte imposes on the layers above
            public float blurAmt;       // Blur channel amount (Eval'd matteBlurAmount)
            public float dispAmt;       // Displace channel amount (Eval'd matteDisplaceAmount)
            public float hueDeg;        // Hue channel rotation degrees (Eval'd matteHueDegrees)
            public bool oneShot;        // NextLayer scope ⇒ cleared after one Draw layer; AllAbove ⇒ persists
            public bool alphaSource;    // slice 5: modulate the mask by the covered layer's own (1 − α) per pixel
        }

        /// Rec. 601 luma, matching how the eye weights the channels; a green flame reads brighter than a blue
        /// one of the same numeric value, which is what you want when authoring a mask by eye.
        static float Luma(Color32 c) => (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) * (1f / 255f);

        static float[] BuildMatteMask(Color32[] src, bool invert, float strength)
        {
            var mask = new float[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                var c = src[i];
                float m = Luma(c) * (c.a * (1f / 255f));
                if (invert) m = 1f - m;
                mask[i] = Mathf.Clamp01(m) * strength;
            }
            return mask;
        }

        static void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
            => Color.RGBToHSV(new Color(r, g, b), out h, out s, out v);

        /// Apply a matte to one layer's isolated buffer, in place, just before it composites. `channel` is a
        /// FLAG SET — every enabled channel acts, in a fixed order: the spatial ones first (they move/soften
        /// pixels), then colour, then alpha last (so a masked-away pixel isn't recoloured pointlessly). None
        /// (an unset field on an old asset) falls back to Alpha, the original single-channel default.
        ///
        /// α-strength source (slice 5): when `alphaSource` is on, the mask is modulated per COVERED pixel by that
        /// pixel's own alpha as (1 − α) — opaque interior ⇒ no effect, soft/thin/edge pixels ⇒ full effect. The
        /// snapshot `alpha0` is taken ONCE HERE, at entry, BEFORE any channel runs, because Blur/Displace rewrite
        /// alpha mid-pipeline and all channels must read the same finished-coverage reference. `effMask` (the
        /// modulated mask) is fed to the five non-Alpha channels; the Alpha channel is DELIBERATELY excluded from
        /// the modulation (clipping alpha hardest where alpha is already lowest is near-degenerate) and always reads
        /// the un-modulated `mask`. When `alphaSource` is off, `effMask` IS `mask` (same reference, no multiply) so
        /// output is byte-identical to the plain luma matte.
        static void ApplyMatte(Color32[] target, float[] mask, MatteChannel channel, float blurAmount,
                               float displaceAmount, float hueDegrees, bool alphaSource, int W, int H)
        {
            if (channel == MatteChannel.None) channel = MatteChannel.Alpha;

            float[] effMask = mask;
            if (alphaSource)
            {
                // Snapshot of each covered pixel's alpha, taken now (entry) — a pure function of the layer's own
                // finished coverage, so fully deterministic. Multiply the mask by (1 − α) once; every channel below
                // then reads this one fixed reference regardless of what Blur/Displace do to target.a afterwards.
                effMask = new float[mask.Length];
                for (int i = 0; i < mask.Length; i++)
                    effMask[i] = mask[i] * (1f - target[i].a * (1f / 255f));
            }

            if ((channel & MatteChannel.Displace) != 0) MatteDisplace(target, effMask, displaceAmount, W, H);
            if ((channel & MatteChannel.Blur) != 0) MatteBlur(target, effMask, blurAmount, W, H);
            if ((channel & MatteChannel.Saturation) != 0) MatteSaturation(target, effMask);
            if ((channel & MatteChannel.Hue) != 0) MatteHue(target, effMask, hueDegrees);
            if ((channel & MatteChannel.Brightness) != 0) MatteBrightness(target, effMask);
            if ((channel & MatteChannel.Alpha) != 0) MatteAlpha(target, mask);   // Alpha excluded from α-source: un-modulated mask
        }

        static void MatteAlpha(Color32[] target, float[] mask)
        {
            for (int i = 0; i < target.Length; i++)
            {
                var c = target[i];
                if (c.a == 0) continue;
                target[i] = new Color32(c.r, c.g, c.b, (byte)Mathf.RoundToInt(c.a * mask[i]));
            }
        }

        static void MatteBrightness(Color32[] target, float[] mask)
        {
            for (int i = 0; i < target.Length; i++)
            {
                var c = target[i];
                if (c.a == 0) continue;
                float m = mask[i];
                target[i] = new Color32((byte)(c.r * m), (byte)(c.g * m), (byte)(c.b * m), c.a);
            }
        }

        static void MatteSaturation(Color32[] target, float[] mask)
        {
            // Mask LOW drains to grey, so a matte reads as "this is where the colour has burned out".
            for (int i = 0; i < target.Length; i++)
            {
                var c = target[i];
                if (c.a == 0) continue;
                float y = Luma(c) * 255f;
                float m = mask[i];
                target[i] = new Color32(
                    (byte)Mathf.Clamp(Mathf.Lerp(y, c.r, m), 0f, 255f),
                    (byte)Mathf.Clamp(Mathf.Lerp(y, c.g, m), 0f, 255f),
                    (byte)Mathf.Clamp(Mathf.Lerp(y, c.b, m), 0f, 255f), c.a);
            }
        }

        static void MatteHue(Color32[] target, float[] mask, float hueDegrees)
        {
            for (int i = 0; i < target.Length; i++)
            {
                var c = target[i];
                if (c.a == 0) continue;
                float m = mask[i];
                if (m <= 0.0001f) continue;
                RgbToHsv(c.r / 255f, c.g / 255f, c.b / 255f, out float h, out float s, out float v);
                h = Mathf.Repeat(h + (hueDegrees / 360f) * m, 1f);
                var rgb = Color.HSVToRGB(h, s, v);
                target[i] = new Color32((byte)(rgb.r * 255f), (byte)(rgb.g * 255f), (byte)(rgb.b * 255f), c.a);
            }
        }

        static void MatteBlur(Color32[] target, float[] mask, float amount, int W, int H)
        {
            // Per-pixel variable radius, so one matte can hold a shape sharp while its surroundings melt.
            // Separable would be faster but is wrong here: the radius differs per pixel, so the two passes
            // wouldn't agree on a kernel. Canvases are small (Pyre is pixel art), the radius is clamped, so a
            // direct box gather is affordable and exact.
            var src = (Color32[])target.Clone();
            int maxR = Mathf.Clamp(Mathf.CeilToInt(amount), 0, 12);
            if (maxR == 0) return;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    int r = Mathf.RoundToInt(amount * mask[i]);
                    if (r <= 0) continue;
                    float ar = 0f, ag = 0f, ab = 0f, aa = 0f, n = 0f;
                    for (int dy = -r; dy <= r; dy++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            int sx = x + dx, sy = y + dy;
                            if (sx < 0 || sy < 0 || sx >= W || sy >= H) continue;
                            var s2 = src[sy * W + sx];
                            float a = s2.a * (1f / 255f);
                            ar += s2.r * a; ag += s2.g * a; ab += s2.b * a; aa += a; n += 1f;
                        }
                    if (n <= 0f || aa <= 0.0001f) continue;
                    target[i] = new Color32((byte)Mathf.Clamp(ar / aa, 0f, 255f),
                                            (byte)Mathf.Clamp(ag / aa, 0f, 255f),
                                            (byte)Mathf.Clamp(ab / aa, 0f, 255f),
                                            (byte)Mathf.Clamp(aa / n * 255f, 0f, 255f));
                }
        }

        static void MatteDisplace(Color32[] target, float[] mask, float amount, int W, int H)
        {
            // Push each pixel along the mask's own SLOPE (its gradient), not along the mask's value — that is
            // what makes it read as refraction: flat regions of the mask don't move at all, and only its
            // EDGES bend what's behind them, exactly like a lens.
            var src = (Color32[])target.Clone();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    int xm = Mathf.Max(0, x - 1), xp = Mathf.Min(W - 1, x + 1);
                    int ym = Mathf.Max(0, y - 1), yp = Mathf.Min(H - 1, y + 1);
                    float gx = mask[y * W + xp] - mask[y * W + xm];
                    float gy = mask[yp * W + x] - mask[ym * W + x];
                    if (gx == 0f && gy == 0f) continue;
                    int sx = Mathf.Clamp(x + Mathf.RoundToInt(gx * amount), 0, W - 1);
                    int sy = Mathf.Clamp(y + Mathf.RoundToInt(gy * amount), 0, H - 1);
                    target[i] = src[sy * W + sx];
                }
        }

        // ── modifiers (T6) ───────────────────────────────────────────────────────────
        // The geometry + pixel modifiers resolved & prepared for the current frame. Sorted/arranged the way
        // BlastRenderer's ModStack is; PostModifiers are NOT here (they run buffer-wide in ApplyPost).
        readonly struct ModSet
        {
            public readonly GeometryModifier[] geo;   // enabled, ascending by WarpPass; InverseWarp applied in REVERSE (highest pass first), mirroring BlastRenderer.ApplyGeo
            public readonly PixelModifier[] pix;      // enabled, list order
            public ModSet(GeometryModifier[] g, PixelModifier[] p) { geo = g; pix = p; }
            public bool AnyGeo => geo != null && geo.Length > 0;
            public bool AnyPix => pix != null && pix.Length > 0;
            public bool Any => AnyGeo || AnyPix;
            public static readonly ModSet Empty = new ModSet(System.Array.Empty<GeometryModifier>(), System.Array.Empty<PixelModifier>());
        }

        // ── spec-wide GLOBAL modifiers (task #56) — the constants that make globals wrap each layer in Pyre1's order.
        // Global GEOMETRY warps get this added to their effective WarpPass so they sort ABOVE every layer modifier —
        // a global warp is the OUTERMOST transform (ApplyGeo walks highest-pass first, so it applies first, wrapping
        // each layer's own warps). Mirrors BlastRenderer.GlobalPassOffset (Runtime/Pyre/BlastRenderer.cs:79).
        const int GlobalPassOffset = 1000;
        // Global modifiers' field-id blocks start here (rebased onto FldModifier + n*8) so they never collide with a
        // layer's 0-based blocks — mirrors BlastRenderer's baseId 500 for globals (BlastRenderer.cs:87).
        const int GlobalModBase = 500;
        // Global POST passes' field-id block base — mirrors BlastRenderer's 700 (BlastRenderer.cs:968).
        const int GlobalPostBase = 700;
        // Globals Eval layer-INDEPENDENTLY: a global modifier warps every layer identically. Mirrors BlastRenderer
        // feeding GlobalLayerId (-1) instead of the real layer index — but in PyrePlus layer identity rides
        // _layerSalt (Eval's d-slot), so we pin _layerSalt to this sentinel while PREPARING globals (Prepare snapshots
        // its ZUIValues synchronously — each modifier caches e(...) immediately) so their MinMax draws don't vary per
        // layer, then restore it. -1 also matches the WriteMatte/global convention already used for GlobalLayerId.
        const int GlobalLayerSalt = -1;

        // A geometry modifier paired with its EFFECTIVE warp pass (WarpPass + a passOffset). Global geo carry +Global-
        // PassOffset so they sort above every layer geo. Mirrors BlastRenderer.GeoEntry.
        struct GeoEntry { public GeometryModifier mod; public int pass; }

        // Collect + Prepare one modifier list into the shared geo/pix buckets — the exact mirror of BlastRenderer.
        // CollectMods. Called TWICE by BuildMods: once for the layer's own modifiers (progress = layerLife, salt =
        // the current _layerSalt, baseId 0, passOffset 0) and once for the spec's globalModifiers (progress = the
        // blast life, salt = GlobalLayerSalt, baseId GlobalModBase, passOffset GlobalPassOffset). geo entries carry
        // the effective pass; pix are appended in list order (so layer pixels precede global pixels — the global
        // pixel effect runs AFTER each layer's own, exactly as BlastRenderer collects them). PostModifiers are skipped
        // (they run buffer-wide — per-layer in ApplyLayerPost, spec-wide in the trailing global post loop). Each
        // modifier's animatable params resolve through the SAME closure shape BlastRenderer builds. EdgeModifier /
        // SimulationModifier have no apply stage in PyrePlus's disc raster, so they are ignored here.
        static void CollectMods(List<PyreModifier> list, int seed, float life, int salt, int baseId, int passOffset,
                                List<GeoEntry> geo, List<PixelModifier> pix)
        {
            if (list == null) return;
            int savedSalt = _layerSalt;
            _layerSalt = salt;   // Prepare's Eval closure reads _layerSalt synchronously (a modifier caches e(...) now)
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                if (m == null || !m.enabled) continue;
                if (m is PostModifier) continue;
                int idx = i;
                m.Prepare((v, fid) => Eval(v, life, seed, ModParticleIndex, FldModifier + (baseId + idx) * 8 + fid));
                if (m is GeometryModifier gm) geo.Add(new GeoEntry { mod = gm, pass = gm.WarpPass + passOffset });
                else if (m is PixelModifier pm) pix.Add(pm);
            }
            _layerSalt = savedSalt;
        }

        // Build the effective per-frame Geometry/Pixel stack for ONE layer: its own modifiers with the spec-wide
        // globalModifiers WRAPPED around them in Pyre1's order (see CollectMods) — the exact mirror of BlastRenderer.
        // BuildStack. globalList EMPTY ⇒ the layer's own mods produce the identical sorted geo array + pix order as
        // before task #56 (globals only ever ADD entries with a pass ≥ GlobalPassOffset, and none are added when the
        // list is empty), so a default spec is BYTE-IDENTICAL. Returns ModSet.Empty when nothing applies (the
        // byte-identical fast path DrawParticle keys off).
        static ModSet BuildMods(List<PyreModifier> layerList, List<PyreModifier> globalList, int seed,
                                float layerLife, float blastLife)
        {
            bool noLayer = layerList == null || layerList.Count == 0;
            bool noGlobal = globalList == null || globalList.Count == 0;
            if (noLayer && noGlobal) return ModSet.Empty;

            var geo = new List<GeoEntry>();
            var pix = new List<PixelModifier>();
            CollectMods(layerList, seed, layerLife, _layerSalt, 0, 0, geo, pix);
            CollectMods(globalList, seed, blastLife, GlobalLayerSalt, GlobalModBase, GlobalPassOffset, geo, pix);
            if (geo.Count == 0 && pix.Count == 0) return ModSet.Empty;

            GeometryModifier[] geoArr;
            if (geo.Count == 0) geoArr = System.Array.Empty<GeometryModifier>();
            else
            {
                // Stable insertion sort ascending by EFFECTIVE pass (mirrors BlastRenderer.SortGeoStable): a higher-
                // pass warp reframes the shape first, so ApplyGeo (walking from the end) applies it OUTERMOST. Global
                // geo (pass ≥ GlobalPassOffset) therefore sort above — and are applied before — every layer geo.
                // Same-pass modifiers keep authoring order (layer geo stay in their original relative order, which is
                // why a globals-empty build reproduces the pre-#56 sort exactly).
                for (int a = 1; a < geo.Count; a++)
                {
                    var e = geo[a];
                    int p = e.pass;
                    int b = a - 1;
                    while (b >= 0 && geo[b].pass > p) { geo[b + 1] = geo[b]; b--; }
                    geo[b + 1] = e;
                }
                geoArr = new GeometryModifier[geo.Count];
                for (int k = 0; k < geo.Count; k++) geoArr[k] = geo[k].mod;
            }
            return new ModSet(geoArr, pix.Count == 0 ? System.Array.Empty<PixelModifier>() : pix.ToArray());
        }

        // Undo the geometry modifiers on a pixel offset from the canvas centre (highest WarpPass first), in
        // shape-aware context — the exact fold BlastRenderer.ApplyGeo performs.
        static Vector2 ApplyGeo(GeometryModifier[] geo, Vector2 off, float phase, in GeoCtx ctx)
        {
            for (int i = geo.Length - 1; i >= 0; i--) off = geo[i].InverseWarp(off, phase, ctx);
            return off;
        }

        // Run the pixel modifiers on one lit pixel; false = drop it. Mirrors BlastRenderer.ApplyPix.
        static bool ApplyPix(PixelModifier[] pix, ref Color col, ref float alpha, int x, int y, float wx, float wy,
                             int frame, float crossFrac, float life, int hash, int W, int H)
        {
            var info = new PixelInfo(x, y, wx, wy, frame, crossFrac, life, hash, W, H);
            for (int i = 0; i < pix.Length; i++)
                if (!pix[i].ApplyPixel(ref col, ref alpha, info)) return false;
            return true;
        }

        // Whole-frame post passes after everything composites, in list order — the stage & keying of
        // BlastRenderer's global PostModifier loop.
        static void ApplyLayerPost(List<PyreModifier> list, int seed, Color32[] buf, int W, int H, float life, int frameIndex)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                if (m == null || !m.enabled) continue;
                if (m is PostModifier post)
                {
                    SetPostContext(post, life, seed, frameIndex);
                    int idx = i;
                    m.Prepare((v, fid) => Eval(v, life, seed, ModParticleIndex, FldModifier + idx * 8 + fid));
                    post.Apply(buf, W, H);
                }
            }
        }

        // PostModifier exposes its per-frame context (life/seed/frameIndex) through INTERNAL setters — BlastRenderer,
        // in the same assembly, calls them directly (SetLife/SetSeed/SetFrameIndex). PyrePlus is a SEPARATE assembly
        // with no InternalsVisibleTo, and may not modify Pyre, so it reaches them by reflection: resolved once,
        // null-guarded so a rename degrades to "context not set" instead of throwing. This keeps seed/frame-dependent
        // post passes deterministic and correct (e.g. Dissolve's per-frame Scatter churn, Kaleidoscope's Vary
        // seeding) exactly as they render inside Pyre.
        static MethodInfo _postSetLife, _postSetSeed, _postSetFrame;
        static bool _postReflectResolved;
        static void SetPostContext(PostModifier post, float life, int seed, int frameIndex)
        {
            if (!_postReflectResolved)
            {
                const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
                var t = typeof(PostModifier);
                _postSetLife = t.GetMethod("SetLife", F);
                _postSetSeed = t.GetMethod("SetSeed", F);
                _postSetFrame = t.GetMethod("SetFrameIndex", F);
                _postReflectResolved = true;
            }
            _postSetLife?.Invoke(post, new object[] { life });
            _postSetSeed?.Invoke(post, new object[] { seed });
            _postSetFrame?.Invoke(post, new object[] { frameIndex });
        }

        // ── per-layer simulation modifier (layer.simulationModifier, slice 7) ────────────────────────────────────
        // Pyre's SimulationModifier is a STATEFUL PyreModifier that retains frame-to-frame state and REPLAYS itself
        // internally: SetSeed sets its blast seed; EnsureFrame(frame, seedBuf, W, H, paramsForFrame) resets+replays
        // 0→frame on any jump (or forward-steps by exactly one otherwise), calling Prepare(paramsForFrame(f)) before
        // every Step so each replayed frame uses ITS OWN animated params; the public Render(scratch, W, H) then paints
        // the current state (PixelFluidModifier advects/erodes the finished layer pixels via the persisted velocity
        // field). This is driven EXACTLY as BlastRenderer drives its own per-layer sim (Runtime/Pyre/BlastRenderer.cs:
        // 156-166), on the layer's isolated scratch. SetSeed + EnsureFrame are INTERNAL on Pyre's SimulationModifier,
        // and PyrePlus is a SEPARATE assembly with no InternalsVisibleTo (and may not modify Pyre), so both are reached
        // by reflection — the same idiom as SetPostContext — resolved once and NULL-GUARDED: if either can't be found
        // the layer degrades to "sim not driven" (Render on a never-stepped instance is an exact no-op — its velocity
        // array is null; see PixelFluidModifier.Render). EnsureFrame is called EVERY frame (it decides forward-step vs
        // replay itself), so a cold scrub straight to frame N reproduces the sequential render's frame N.
        static MethodInfo _simSetSeed, _simEnsureFrame;
        static bool _simReflectResolved;
        static void ApplyLayerSim(SimulationModifier sim, int seed, Color32[] scratch, int W, int H, int frameIndex, int frames)
        {
            if (sim == null) return;
            if (!_simReflectResolved)
            {
                const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
                var t = typeof(SimulationModifier);
                _simSetSeed = t.GetMethod("SetSeed", F);
                _simEnsureFrame = t.GetMethod("EnsureFrame", F);
                _simReflectResolved = true;
            }
            if (_simSetSeed == null || _simEnsureFrame == null) return;   // reflection failed → not driven
            _simSetSeed.Invoke(sim, new object[] { seed });
            // paramsForFrame(f): resolve THIS modifier's ZUIValues as frame f itself would — EnsureFrame calls it once
            // per Step, INCLUDING for the frames it replays on a cold jump, so a replayed history uses each of its own
            // frames' params (not whatever frameIndex resolves to). fp is frame f's life; the sim modifier's own fields
            // Eval frame-global (ModParticleIndex) through the FldSimModifier block. Mirrors BlastRenderer's
            // layerSimParamsForFrame closure. _layerSalt is still THIS layer's index here (the closure runs
            // synchronously inside EnsureFrame within this call), so two sim layers never share an RNG stream.
            System.Func<int, System.Func<ZUIValue, int, float>> paramsForFrame = f =>
            {
                float fp = frames > 1 ? f / (float)(frames - 1) : 0f;
                return (v, fid) => Eval(v, fp, seed, ModParticleIndex, FldSimModifier + fid);
            };
            _simEnsureFrame.Invoke(sim, new object[] { frameIndex, scratch, W, H, paramsForFrame });
            sim.Render(scratch, W, H);
        }

        // ── swarm ──────────────────────────────────────────────────────────────────

        static void RenderSwarm(Color32[] buf, int W, int H, float life, PyrePlusSpec spec, PyrePlusLayer layer,
                                in ModSet mods, float phase, int frameIndex)
        {
            var spawns = new List<SpawnPoint>(Mathf.Max(2, layer.swarmCount));
            ComputeSpawns(spec, layer, spawns);

            // Scale-by-index (S1): a per-particle size multiplier chosen by index. Static 1 (the default) is an
            // EXACT no-op — IsStaticOne gates it out so sizeMul is untouched and the swarm stays byte-identical.
            bool scaleIdx = !IsStaticOne(layer.swarmScaleByIndex);
            // Die-together (S1): every particle's shared death point on the blast timeline. Off ⇒ the per-particle
            // life below is the verbatim pre-S1 expression (byte-identical).
            bool dieTogether = layer.swarmDieTogether;
            // The shared death point = the LAST particle's spawn moment + particleLife, clamped to 1. maxSpawnLife
            // replaces the old swarmSpawnWindow term (now removed): for Window timing it's the canonical
            // (MinMax→midpoint) spawn-timing envelope at frac 1 — 0.5 for the default (0,0)→(1,0.5), i.e. the old
            // 0.5 window, so a default die-together swarm is unchanged — and for FrameStep it's the last particle's
            // own frame mapped to [0,1]. Off ⇒ deathPoint 0 (unused).
            float deathPoint = 0f;
            if (dieTogether)
            {
                int lastIdx = Mathf.Max(0, spawns.Count - 1);
                float maxSpawnLife = layer.swarmTiming == SwarmTiming.FrameStep
                    ? Mathf.Clamp01((layer.swarmFirstFrame + lastIdx * layer.swarmFrameStep) / (float)Mathf.Max(1, spec.frameCount - 1))
                    : Mathf.Clamp01(EvalCanonical(layer.swarmSpawnTiming, 1f));
                deathPoint = Mathf.Min(1f, maxSpawnLife + layer.swarmParticleLife);
            }

            // Swarm live rotation (Issue 2B): a rigid whole-cloud rotation evaluated at the CURRENT frame's `life`
            // (NOT a per-spawn snapshot like the spawner rotation), applied UNIFORMLY to every already-placed
            // particle so the whole arrangement spins together as one solid group. It reuses the spawner's
            // yaw/pitch/roll projection (ApplyShapeTransform) with turn = yaw, tilt = pitch, roll = Z, rotating
            // each particle around the shape centre. Frame-global, so it Evals ONCE here with the modifier-scope
            // sentinel index — a MinMax spin is ONE value for the whole cloud, not a per-particle draw. All-zero
            // (the default) is an EXACT no-op: swarmRot stays false, positions are untouched, and the swarm renders
            // byte-identical (Eval of a Static-0 ZUIValue returns exactly 0 with no RNG draw).
            float cx = spec.Width * 0.5f, cy = spec.Height * 0.5f;
            float sTurn = Eval(layer.swarmTurn, life, spec.seed, ModParticleIndex, FldSwarmTurn);
            float sTilt = Eval(layer.swarmTilt, life, spec.seed, ModParticleIndex, FldSwarmTilt);
            float sRoll = Eval(layer.swarmRoll, life, spec.seed, ModParticleIndex, FldSwarmRoll);
            bool swarmRot = sTurn != 0f || sTilt != 0f || sRoll != 0f;
            // Swarm live SCALE (slice 0): the sibling of the spin above — a uniform RADIAL scale of the placed cloud
            // about its centre, evaluated at the SAME frame-global life clock (the modifier-scope sentinel index; a
            // MinMax scale is ONE value for the whole cloud). Applied AFTER the spin, at the same stage: final live
            // position = centre + scale·spin(offset). Static 1 (the default) Evals to exactly 1 → swarmScl false →
            // the multiply is skipped, so a default/existing swarm renders byte-identical.
            float sScale = Eval(layer.swarmScale, life, spec.seed, ModParticleIndex, FldSwarmScale);
            bool swarmScl = sScale != 1f;

            // ── Coalesce = Fuse (slice 1) / Ramp (slice 2) ───────────────────────────────────────────────
            // When the layer coalesces, the loop below does NOT Over-composite each particle; it COLLECTS every alive
            // particle (its own-life size + alpha, mirroring DrawParticle's first lines) into a field-particle set,
            // then a single post-loop field-pass reads the WHOLE set. Fuse collects metaball circles → RenderPlus-
            // FusedField (sum → threshold → shade one iso-surface, MetaBlob). Ramp additionally Eval's the two per-
            // particle weights density/heat → RenderPlusRampField (three SmoothMax dome fields → relief light → one
            // smoke→fire cloud, HeightBalls). Each list is only allocated for its own mode, so Off is byte-identical
            // to before and Fuse is unchanged. cx/cy above are the canvas centre — collected positions are stored
            // centre-relative (the space the field-passes sample in).
            bool fuse = layer.coalesce == LayerCoalesce.Fuse;
            bool ramp = layer.coalesce == LayerCoalesce.Ramp;
            List<FieldParticle> fieldParts = fuse ? new List<FieldParticle>(Mathf.Max(2, spawns.Count)) : null;
            List<RampParticle> rampParts = ramp ? new List<RampParticle>(Mathf.Max(2, spawns.Count)) : null;

            for (int i = 0; i < spawns.Count; i++)
            {
                var sp = spawns[i];
                // Each particle's own life clock: 0 at its spawn frame, 1 at its death. Skip when not alive yet
                // or already dead this frame. All Shape fields (size/alpha/colour) then evaluate at `own`, exactly
                // as the single particle evaluates them at `life`. Die-together (S1) instead maps every particle's
                // clock onto the SHARED deathPoint so they all reach own==1 at the same frame (guarding the tiny
                // denominator when a particle spawns at/after the death point); off ⇒ the original per-particle form.
                float own = dieTogether
                    ? (life - sp.spawnLife) / Mathf.Max(0.0001f, deathPoint - sp.spawnLife)
                    : (life - sp.spawnLife) / Mathf.Max(0.0001f, layer.swarmParticleLife);
                if (own < 0f || own > 1f) continue;
                // Depth shading from the spawn-time tilt: nearer parts (zNorm > 0) draw bigger and brighter, far
                // parts smaller and dimmer. Both multipliers are exactly 1 at zNorm == 0, so an untilted swarm
                // renders byte-identical to the pre-T3 output.
                float sizeMul = Mathf.Clamp(1f + 0.35f * sp.zNorm, 0.5f, 1.6f);
                float brightMul = Mathf.Clamp(1f + 0.30f * sp.zNorm, 0.55f, 1.45f);
                // Length-only index taper (slice 3 — Bars barTaper): a separate LENGTH multiplier for the Streak form.
                // Default 1 (an exact no-op passed to DrawStreakBody, where len·1 is bit-exact). When the layer opts
                // into streakScaleLengthOnly AND the form is Streak, the per-index multiplier drives length ONLY (it
                // goes here, NOT into sizeMul), so width stays on the plain sizeMul — equal-width bars of graduated
                // length. Otherwise it folds into sizeMul exactly as before (both length and width), byte-identical.
                float lenByIndex = 1f;
                if (scaleIdx)
                {
                    // Curve INPUT is the index fraction i/(n-1) (like spawn timing), NOT life — a per-particle
                    // shape, evaluated once. n>=1; a single-particle swarm (Text) uses input 0.
                    float t = spawns.Count > 1 ? i / (float)(spawns.Count - 1) : 0f;
                    float idxScale = Mathf.Max(0f, Eval(layer.swarmScaleByIndex, t, spec.seed, i, FldScaleByIndex));
                    if (layer.streakScaleLengthOnly && layer.shapeForm == ShapeForm.Streak)
                        lenByIndex *= idxScale;   // LENGTH only — width unaffected (the barTaper silhouette)
                    else
                        sizeMul *= idxScale;      // BOTH length and width (the pre-slice-3 path, byte-identical)
                }
                // Rotate the whole cloud live (Issue 2B) — around the shape centre, at the frame's life. Normalize
                // depth against THIS particle's radial distance so the sweep is uniform across the cloud (a particle
                // twice as far turns through the same angle); offX/offY = 0 → the rotation adds no translation.
                // swarmRot false ⇒ the exact spawn position, byte-identical.
                Vector2 dp = sp.pos;
                if (swarmRot)
                    dp = ApplyShapeTransform(sp.pos, cx, cy, Vector2.Distance(sp.pos, new Vector2(cx, cy)),
                                             sRoll, sTurn, sTilt, 0f, 0f, out _);
                // Live whole-cloud radial scale (slice 0), applied AFTER the spin so the final position is
                // centre + scale·spin(offset). swarmScl false (default scale 1) ⇒ dp untouched, byte-identical.
                if (swarmScl)
                    dp = new Vector2(cx + (dp.x - cx) * sScale, cy + (dp.y - cy) * sScale);

                // ── Coalesce seam (slice 0 → Fuse slice 1 → Ramp slice 2) ────────────────────────────────────
                // Off (default) → per-particle Over-compositing (DrawParticle below), byte-identical to
                // pre-Coalesce. Fuse/Ramp COLLECT this particle instead of drawing it (the field pass runs after the
                // loop). The form is ignored in both modes: a coalescing particle is always a circle/dome.
                if (fuse)
                {
                    // Evaluate size + alpha at THIS particle's OWN life exactly as DrawParticle's Disc path does
                    // (radius = size·sizeMul with the same >0.01 guard; alpha = the alpha envelope with the same
                    // >0.002 guard) — the fuse weight IS that own-life alpha (= FusionCircle.weight in Pyre1).
                    float fr = Mathf.Max(0f, Eval(layer.size, own, spec.seed, i, FldSize)) * sizeMul;
                    if (fr > 0.01f)
                    {
                        float fa = Mathf.Clamp01(Eval(layer.alpha, own, spec.seed, i, FldAlpha));
                        if (fa > 0.002f) fieldParts.Add(new FieldParticle(dp.x - cx, dp.y - cy, fr, fa));
                    }
                    continue;   // no per-particle draw — the whole set is fused after the loop
                }
                if (ramp)
                {
                    // Same radius (size·sizeMul) + alpha as Fuse, PLUS the two Ramp-only per-particle weights the
                    // plain swarm doesn't carry: density (mass) and heat (height/energy), each an own-life envelope
                    // Eval'd here beside size/alpha (their own field ids -25/-26). Guard mirrors Pyre1's AddHeightBall
                    // (BlastRenderer.cs:1584): an invisible OR massless+heatless ball must not exist — it would still
                    // drag the fused opacity of whatever it overlaps down toward nothing.
                    float rr = Mathf.Max(0f, Eval(layer.size, own, spec.seed, i, FldSize)) * sizeMul;
                    if (rr > 0.01f)
                    {
                        float ra = Mathf.Clamp01(Eval(layer.alpha, own, spec.seed, i, FldAlpha));
                        float rd = Mathf.Max(0f, Eval(layer.density, own, spec.seed, i, FldRampDensity));
                        float rh = Mathf.Max(0f, Eval(layer.heat, own, spec.seed, i, FldRampHeat));
                        if (ra > 0.002f && (rd > 0.0005f || rh > 0.0005f))
                            rampParts.Add(new RampParticle(dp.x - cx, dp.y - cy, rr, rd, rh, ra));
                    }
                    continue;   // no per-particle draw — the whole set is fused after the loop
                }
                DrawParticle(buf, W, H, dp.x, dp.y, own, spec, layer, i, mods, phase, frameIndex,
                             sizeMul, brightMul, sp.orientDeg, lenByIndex);
            }
            // Fuse (MetaBlob) field-pass: sum → threshold → gradient-shade the whole collected set into `buf`, on the
            // layer's isolated scratch, BEFORE ApplyLayerPost (so Post modifiers still shape the fused result). Off
            // leaves fieldParts null and skips this entirely, so it renders exactly as before.
            if (fuse)
                RenderPlusFusedField(buf, W, H, life, spec, layer, mods, phase, frameIndex, fieldParts, cx, cy);
            // Ramp (HeightBalls) field-pass: three SmoothMax dome fields → relief light → one smoke→fire cloud, on the
            // same isolated scratch, BEFORE ApplyLayerPost. Off/Fuse leave rampParts null and skip this.
            else if (ramp)
                RenderPlusRampField(buf, W, H, life, spec, layer, mods, phase, frameIndex, rampParts, cx, cy);
        }

        // ── Fuse (MetaBlob) field-pass (slice 1) ─────────────────────────────────────────────────────────────
        // The STATELESS Coalesce == Fuse render mode. After RenderSwarm's loop collected every alive particle as a
        // FieldParticle{pos, radius, weight}, this reads the WHOLE set as ONE scalar field and composites a single
        // merged, gradient-shaded silhouette — a metaball union — instead of Over-compositing each particle. Ported
        // near-verbatim from BlastRenderer.RenderFusedField (Runtime/Pyre/BlastRenderer.cs:1957), with the kernel
        // sum + threshold/band/frac math pulled into the shared PyrePlusField substrate (so slice 2's Ramp and
        // slice 4's matte heightmap reuse the same primitives). Per pixel:
        //     field = Σ weight·(1−d²/r²)²                       (PyrePlusField.Sample)
        //     threshold = max(0.02, fuseThreshold)
        //     band      = clamp(fuseSoftness, 0.01, threshold)
        //     discard where field ≤ threshold−band
        //     alpha = clamp01((field−(threshold−band))/band) · fillColour.a   (ThresholdShade gives the AA band)
        //     frac  = clamp01((field−threshold)/fuseShadeRange)               (0 = surface, 1 = core)
        //     colour = shapeFill gradient at frac
        // Geometry modifiers fold each sample point (bending the fused field), pixel modifiers recolour/drop each lit
        // pixel — the same stack DrawParticle applies, matching RenderFusedField. Deterministic: reads only the
        // seeded ComputeSpawns placements. (The "·layerAlpha" in the design resolves to the fill's own alpha here,
        // exactly as RenderFusedField composites `a * fc.a`; PyrePlus has no separate whole-layer opacity field, and
        // the per-particle alpha already fades the blob by shrinking each circle's field weight.)
        static void RenderPlusFusedField(Color32[] buf, int W, int H, float life, PyrePlusSpec spec, PyrePlusLayer layer,
                                         in ModSet mods, float phase, int frameIndex, List<FieldParticle> parts, float cx, float cy)
        {
            int n = parts.Count;
            if (n == 0) return;

            // Field bbox → GeoCtx (mirrors RenderFusedField): geometry warps fold around the fused shape's own
            // centre/radius, not each particle's. Positions are already centre-relative.
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                var c = parts[i];
                if (c.x - c.radius < minX) minX = c.x - c.radius;
                if (c.x + c.radius > maxX) maxX = c.x + c.radius;
                if (c.y - c.radius < minY) minY = c.y - c.radius;
                if (c.y + c.radius > maxY) maxY = c.y + c.radius;
            }
            Vector2 fieldCenter = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            float fieldRadius = 0.5f * Mathf.Max(maxX - minX, maxY - minY);
            var ctx = new GeoCtx(cx, cy, fieldCenter, fieldRadius);

            var fill = layer.shapeFill;
            // A stable per-LAYER hash for the pixel modifiers / any noise (frame-global — the fuse pass has no single
            // particle index). Its own field id (FldFuseHash) keeps it decorrelated from the per-particle streams.
            int hash = Hash(spec.seed, ModParticleIndex, FldFuseHash, _layerSalt);
            bool anyGeo = mods.AnyGeo, anyPix = mods.AnyPix;

            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    Vector2 off = new Vector2((x + 0.5f) - cx, (y + 0.5f) - cy);
                    if (anyGeo) off = ApplyGeo(mods.geo, off, phase, ctx);

                    float field = PyrePlusField.Sample(parts, off.x, off.y);
                    if (!PyrePlusField.ThresholdShade(field, layer.fuseThreshold, layer.fuseSoftness, layer.fuseShadeRange,
                                                      out float a, out float frac))
                        continue;

                    // Shade by the shapeFill gradient at frac (surface → core). For OverLife/Solid this is the
                    // gradient/colour at frac; a spatial fill degrades to its centre-line sample — acceptable, and
                    // the common fuse fills are gradients. Depth brightness (brightMul) is intentionally not carried:
                    // fuse merges silhouettes, colour comes from the field, not per-particle depth.
                    Color col = fill != null ? fill.Evaluate(frac, 0f, 0f) : Color.white;
                    float outA = a * col.a;
                    if (outA <= 0.002f) continue;

                    if (anyPix)
                    {
                        // RasterShape convention (matches DrawParticle's modifier path): colour carries RGB with
                        // alpha 1, the real pixel alpha rides separately; both are handed to the pixel modifiers.
                        Color pc = new Color(col.r, col.g, col.b, 1f);
                        float pa = outA;
                        if (!ApplyPix(mods.pix, ref pc, ref pa, x, y, cx + off.x, cy + off.y, frameIndex, frac, life, hash, W, H)) continue;
                        Over(buf, y * W + x, pc.r, pc.g, pc.b, pa);
                    }
                    else Over(buf, y * W + x, col.r, col.g, col.b, outA);
                }
        }

        // ── Ramp (HeightBalls) field-pass (slice 2) ──────────────────────────────────────────────────────────
        // The STATELESS Coalesce == Ramp render mode — HeightBalls decomposed as swarm + a heavier field-pass.
        // After RenderSwarm's loop collected every alive particle as a RampParticle{pos, radius, density, heat,
        // alpha}, this fuses the WHOLE set into three shared scalar fields, lights the height field's relief, and
        // shades one merged smoke→fire cloud — instead of Over-compositing each particle. Ported faithfully from
        // BlastRenderer.RenderHeightBalls (Runtime/Pyre/BlastRenderer.cs:1763), with the SmoothMax dome-accumulate
        // and slope-relief lighting pulled into the shared PyrePlusField substrate (so slice 4's matte heightmap
        // reuses the same primitives). Three passes:
        //   Pass 1  fuse density / heat / height via SmoothMax domes (s = √(1−q)); a shared surface-noise rim scales
        //           each dome's q so neighbouring domes bulge/pinch together and read as one boiling mass (rampRimScale).
        //   Pass 2  relief light = 0.18 + max(0, n·L)·0.82 from the height field's local slope (rampLighting/Relief/Angle).
        //   Pass 3  value = clamp01(density+heat)·light → shapeFill gradient; opacity = clamp01(max(density,heat)·
        //           coverage) · per-pixel blended particle alpha · fill alpha.
        // Deterministic: reads only the seeded ComputeSpawns placements. DELIBERATE LOSSES vs Pyre1's HeightBalls,
        // left OUT of scope for this decomposition (documented, not forced — see PYREPLUS_ADVANCED_DESIGN.md
        // Capability 3): per-ball SQUASH ellipses (PyrePlus domes are circular), idle-boil CHURN, the hbFold
        // confinement fold-under, and single-pass CROSS-GROUP fusion (a PyrePlus layer is ONE swarm population;
        // several strata melting into one mass would need multi-sub-population support). Geometry modifiers do NOT
        // warp the fused field in this slice (the shared accumulate is geometry-agnostic, matching PyrePlusField.
        // Accumulate/AccumulateDomes); pixel modifiers still recolour/drop each lit pixel, and Post modifiers still
        // shape the finished layer via ApplyLayerPost.
        static void RenderPlusRampField(Color32[] buf, int W, int H, float life, PyrePlusSpec spec, PyrePlusLayer layer,
                                        in ModSet mods, float phase, int frameIndex, List<RampParticle> parts, float cx, float cy)
        {
            int n = parts.Count;
            if (n == 0) return;

            // Per-channel melt knee, sized RELATIVE to the strongest ball in each channel (heat runs several times
            // larger than mass, so one shared constant would melt mass but leave heat an almost-hard max) — mirrors
            // RenderHeightBalls (:1791-1805), so the Fusion dial means the same regardless of how hot the cloud is set.
            float fusion = Mathf.Max(0f, layer.rampFusion);
            float maxD = 0f, maxHe = 0f, maxHi = 0f;
            for (int i = 0; i < n; i++)
            {
                var b = parts[i];
                float hi = b.density * 2.65f + b.heat * 0.72f;
                if (b.density > maxD) maxD = b.density;
                if (b.heat > maxHe) maxHe = b.heat;
                if (hi > maxHi) maxHi = hi;
            }
            float kD = fusion * maxD, kHe = fusion * maxHe, kHi = fusion * maxHi;

            // Surface-noise rim (the boiling-mass deform): a per-pixel field SHARED by every dome and every channel,
            // so neighbouring domes bulge/pinch TOGETHER (rimInv2 scales each dome's q) and the interior height gains
            // matching roughness (heightRough) — exactly why the balls interlock into one mass instead of each
            // wobbling on its own. Sampled ONCE per pixel here (not per dome, not per field). Drifts over the layer's
            // life so the surface roils. rampRimScale ≤ 0 ⇒ null (plain circles), an exact no-op.
            float rimAmp = Mathf.Clamp01(layer.rampRimScale);
            float[] rimInv2 = null, heightRough = null;
            if (rimAmp > 0.001f)
            {
                rimInv2 = new float[W * H];
                heightRough = new float[W * H];
                int nseed = Hash(spec.seed, ModParticleIndex, FldRampHash, _layerSalt);
                const float zoom = 14f;                       // feature size in px (matches Pyre1's surfaceZoom default)
                float driftX = life * 8f, driftY = life * -5f;  // roil over the layer's life (pure function of life)
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int i = y * W + x;
                        float ox = (x + 0.5f) - cx, oy = (y + 0.5f) - cy;
                        float surf = PyrePlusField.Noise01(ox / zoom + driftX, oy / zoom + driftY, nseed) * 2f - 1f;
                        float rimScale = 1f + surf * rimAmp;
                        rimInv2[i] = 1f / Mathf.Max(0.05f, rimScale * rimScale);
                        heightRough[i] = 1f + surf * rimAmp * 0.55f;
                    }
            }

            // Pass 1 — fuse the three fields via the shared SmoothMax dome accumulate. Height weights heat well above
            // density (mass·2.65 + heat·0.72), so an energised ball stands TALLER and catches more relief light. The
            // three dome lists share position/radius; only the per-dome weight differs (density / heat / combined).
            var density = new float[W * H];
            var heat = new float[W * H];
            var height = new float[W * H];
            var dParts = new List<FieldParticle>(n);
            var heParts = new List<FieldParticle>(n);
            var hiParts = new List<FieldParticle>(n);
            for (int i = 0; i < n; i++)
            {
                var b = parts[i];
                dParts.Add(new FieldParticle(b.x, b.y, b.radius, b.density));
                heParts.Add(new FieldParticle(b.x, b.y, b.radius, b.heat));
                hiParts.Add(new FieldParticle(b.x, b.y, b.radius, b.density * 2.65f + b.heat * 0.72f));
            }
            PyrePlusField.AccumulateDomes(density, W, H, dParts, kD, cx, cy, rimInv2);
            PyrePlusField.AccumulateDomes(heat, W, H, heParts, kHe, cx, cy, rimInv2);
            PyrePlusField.AccumulateDomes(height, W, H, hiParts, kHi, cx, cy, rimInv2);
            // Interior roughness (height takes the noise directly where cloud is present) + clamp all three to 0..1.
            for (int i = 0; i < W * H; i++)
            {
                if (heightRough != null && height[i] > 0f) height[i] *= heightRough[i];
                density[i] = Mathf.Clamp01(density[i]);
                heat[i] = Mathf.Clamp01(heat[i]);
                height[i] = Mathf.Clamp01(height[i]);
            }

            // Opacity — per pixel, blend whichever balls reach it, weighted by coverage AND their own alpha, so a
            // dying ball can't drag a solid one it overlaps down with it (every particle fades through its own alpha).
            // Mirrors RenderHeightBalls' aWeight/aSum (:1858-1889) using the SAME shared rim deform as the fields.
            var groupAlpha = new float[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int idx = y * W + x;
                    float ox = (x + 0.5f) - cx, oy = (y + 0.5f) - cy;
                    float inv = rimInv2 != null ? rimInv2[idx] : 1f;
                    float aWeight = 0f, aSum = 0f;
                    for (int i = 0; i < n; i++)
                    {
                        var b = parts[i];
                        float r2 = b.radius * b.radius;
                        if (r2 <= 0f) continue;
                        float dx = ox - b.x, dy = oy - b.y;
                        float q = (dx * dx + dy * dy) / r2 * inv;
                        if (q >= 1f) continue;
                        float s = Mathf.Sqrt(1f - q);
                        float w = s * (b.density + b.heat) * b.alpha;
                        aWeight += w;
                        aSum += w * b.alpha;
                    }
                    groupAlpha[idx] = aWeight > 1e-6f ? aSum / aWeight : 0f;
                }

            // Pass 2 — relief lighting from the height field's local slope (the shared substrate helper).
            float[] light = null;
            if (layer.rampLighting)
            {
                light = new float[W * H];
                PyrePlusField.ReliefLight(light, height, W, H, layer.rampLightAngle, layer.rampRelief);
            }

            // Pass 3 — shade. Combined density+heat (lit) picks the gradient position; whichever field is stronger,
            // scaled by coverage and the blended particle alpha, is the opacity. The lit range is deliberately wide
            // (≈0.35×..1.5×) so the resting cloud, which sits low on the ramp, still shows its carved relief.
            var fill = layer.shapeFill;
            float coverage = Mathf.Max(0.1f, layer.rampCoverage);
            int hash = Hash(spec.seed, ModParticleIndex, FldRampHash, _layerSalt);
            bool anyPix = mods.AnyPix;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    float occupied = Mathf.Max(density[i], heat[i]);
                    if (occupied < 0.004f) continue;
                    float value = Mathf.Clamp01(density[i] + heat[i]);
                    if (light != null) value = Mathf.Clamp01(value * (0.35f + light[i] * 1.15f));
                    Color col = fill != null ? fill.Evaluate(value, 0f, 0f) : Color.white;
                    float outA = Mathf.Clamp01(occupied * coverage) * groupAlpha[i] * col.a;
                    if (outA <= 0.003f) continue;

                    if (anyPix)
                    {
                        // RasterShape convention (matches DrawParticle / the Fuse pass): colour carries RGB with
                        // alpha 1, the real pixel alpha rides separately; both are handed to the pixel modifiers.
                        Color pc = new Color(col.r, col.g, col.b, 1f);
                        float pa = outA;
                        if (!ApplyPix(mods.pix, ref pc, ref pa, x, y, x + 0.5f, y + 0.5f, frameIndex, value, life, hash, W, H)) continue;
                        Over(buf, i, pc.r, pc.g, pc.b, pa);
                    }
                    else Over(buf, i, col.r, col.g, col.b, outA);
                }
        }

        // ── matte heightmap consumer (slice 4b) ─────────────────────────────────────────────────────────────────
        // The PULL twin of the Ramp field-pass (RenderPlusRampField above): instead of BUILDING a height field from a
        // swarm of domes, it reads the ALREADY-FUSED scalar field in `field` — a matte channel the WriteMatte
        // luminance layers below deposited into (several of them combined by Max/Add/Subtract into ONE heightmap) —
        // relief-lights it from its own local slope (the SHARED PyrePlusField.ReliefLight substrate, the same helper
        // Ramp's Pass 2 uses), shades the scalar through this layer's Fill gradient, and Over-composites ONE fused
        // surface. So authored matte layers become a single lit heightmap (bright/lit where their luminance piles up),
        // exactly the HeightBalls look but fed by mattes, not a swarm. Reads only the channel + two plain-float knobs
        // (heightRelief / heightLightAngle) — no Eval, no per-particle state — so it bakes/scrubs identically. Draws
        // NOTHING where the field is 0, so an unfed consumer (or an empty channel) is inert.
        static void RenderHeightConsumer(Color32[] buf, int W, int H, PyrePlusLayer layer, float[] field)
        {
            if (field == null) return;
            // Relief lighting from the fused field's own slope. heightRelief ≈ 0 ⇒ skip it (a flat gradient-mapped
            // field, no carved highlights) — the same shared helper, and the same slope→normal→Lambert math, Ramp uses.
            float[] light = null;
            if (layer.heightRelief > 0.0001f)
            {
                light = new float[W * H];
                PyrePlusField.ReliefLight(light, field, W, H, layer.heightLightAngle, layer.heightRelief);
            }
            var fill = layer.shapeFill;
            for (int i = 0; i < field.Length; i++)
            {
                float h = field[i];
                if (h <= 0.003f) continue;                 // no surface where nothing was deposited
                // Sample the Fill at the RAW fused height (its true position on the ramp), THEN SHADE the resulting
                // colour by the relief-light term (#58 Fix 1). Shifting only the gradient LOOKUP position was invisible
                // on a smooth heightmap — the ramp is smooth, so a smooth-slope lookup shift produced a plain fading
                // gradient. Multiplying the OUTPUT brightness by 0.35 + light·1.15 (the same spread the Ramp pass uses,
                // ~[0.56× shadow .. 1.5× highlight]) makes slopes facing the light visibly brighten and slopes facing
                // away darken — real relief you can see. Over() clamps each channel, so a >1 highlight blows to white.
                Color col = fill != null ? fill.Evaluate(h, 0f, 0f) : Color.white;
                if (light != null)
                {
                    float shade = 0.35f + light[i] * 1.15f;
                    col.r *= shade; col.g *= shade; col.b *= shade;
                }
                // The field IS the surface: its raw (un-lit) value is the coverage, so the surface stays solid in
                // shadow and fades only where the fused luminance itself fades (a soft rim). × the gradient's own alpha.
                float outA = Mathf.Clamp01(h) * col.a;
                if (outA <= 0.003f) continue;
                Over(buf, i, col.r, col.g, col.b, outA);
            }
        }

        /// FIX 3 — apply the layer's LIVE whole-cloud swarm spin (swarmTurn/Tilt/Roll evaluated at `life`) to an
        /// already-placed particle position, EXACTLY as RenderSwarm applies it at draw time: a rigid rotation of the
        /// point around the shape centre (rot = Roll, yaw = Turn, pitch = Tilt), depth-normalized against the point's
        /// own radial distance so the sweep is uniform across the cloud. Public so the editor overlay's spawn dots
        /// reflect the same live spin the render draws (the overlay is editor-only, so calling a renderer helper is
        /// fine). Pure/deterministic — uses the SAME seeded per-frame Eval RenderSwarm uses (a MinMax spin is one
        /// value for the whole cloud). ALL-ZERO spin ⇒ returns `pos` unchanged (the exact no-op guard RenderSwarm
        /// uses), so a non-spinning swarm's overlay is byte-identical. NOT called by RenderFrame — the bake path is
        /// untouched; this only DUPLICATES RenderSwarm's inline spin math rather than replacing it, to keep the bake
        /// provably byte-identical.
        public static Vector2 ApplySwarmSpin(PyrePlusSpec spec, PyrePlusLayer layer, Vector2 pos, float life)
        {
            if (spec == null || layer == null) return pos;
            float sTurn = Eval(layer.swarmTurn, life, spec.seed, ModParticleIndex, FldSwarmTurn);
            float sTilt = Eval(layer.swarmTilt, life, spec.seed, ModParticleIndex, FldSwarmTilt);
            float sRoll = Eval(layer.swarmRoll, life, spec.seed, ModParticleIndex, FldSwarmRoll);
            if (sTurn == 0f && sTilt == 0f && sRoll == 0f) return pos;
            float cx = spec.Width * 0.5f, cy = spec.Height * 0.5f;
            return ApplyShapeTransform(pos, cx, cy, Vector2.Distance(pos, new Vector2(cx, cy)),
                                       sRoll, sTurn, sTilt, 0f, 0f, out _);
        }

        /// Slice 0 — apply the layer's LIVE whole-cloud swarm SCALE (swarmScale at `life`) to an already-placed
        /// particle position, EXACTLY as RenderSwarm applies it at draw time: a uniform radial scale of the point
        /// about the shape centre. The editor-overlay twin of the inline scale in RenderSwarm (sibling to
        /// ApplySwarmSpin), so the spawn dots + trace track the rendered particles when the swarm scales. Call it
        /// AFTER ApplySwarmSpin (final = centre + scale·spin(offset)). scale == 1 (the default) ⇒ returns `pos`
        /// unchanged (the exact no-op guard RenderSwarm uses), so a non-scaling swarm's overlay is byte-identical.
        /// NOT called by RenderFrame — the bake path is untouched; this only DUPLICATES RenderSwarm's inline scale.
        public static Vector2 ApplySwarmScale(PyrePlusSpec spec, PyrePlusLayer layer, Vector2 pos, float life)
        {
            if (spec == null || layer == null) return pos;
            float s = Eval(layer.swarmScale, life, spec.seed, ModParticleIndex, FldSwarmScale);
            if (s == 1f) return pos;
            float cx = spec.Width * 0.5f, cy = spec.Height * 0.5f;
            return new Vector2(cx + (pos.x - cx) * s, cy + (pos.y - cy) * s);
        }

        /// Compute every swarm particle's spawn life (its point on the blast timeline) and spawn POSITION
        /// (absolute canvas-pixel coords). Pure and deterministic — RenderSwarm consumes it, and the preview
        /// overlay (T5) calls it directly to draw a dot at each particle's real spawn location. Fills `into`
        /// (cleared first); allocation-light beyond the list's own growth. Empty when the swarm is off.
        public static void ComputeSpawns(PyrePlusSpec spec, PyrePlusLayer layer, List<SpawnPoint> into)
        {
            if (into == null) return;
            into.Clear();
            if (spec == null || layer == null || !layer.swarmEnabled) return;
            // Set the layer's decorrelation salt from its list position, so a placement the preview overlay computes
            // for the SELECTED layer keys the same RNG streams the render used for that layer (RenderFrame already
            // set _layerSalt == li before calling this via RenderSwarm; the overlay call re-derives the same index).
            int liSalt = spec.layers != null ? spec.layers.IndexOf(layer) : 0;
            _layerSalt = liSalt < 0 ? 0 : liSalt;

            // Text overrides the swarm count: one particle per CHARACTER (min 1), so the swarm places exactly the
            // string's letters. An exact guarded branch — every other form keeps `Mathf.Max(2, swarmCount)` VERBATIM
            // so its spawn arithmetic is byte-identical (orchestrator hash-gated). Text may make n == 1 (one char),
            // which the (n-1) divisions below now special-case.
            int n = layer.shapeForm == ShapeForm.Text
                ? Mathf.Max(1, (layer.textString ?? "").Length)
                : Mathf.Max(2, layer.swarmCount);
            float cx = spec.Width * 0.5f;
            float cy = spec.Height * 0.5f;
            int frames = spec.frameCount;   // FrameStep timing maps a particle's frame index onto the [0,1] life

            for (int i = 0; i < n; i++)
            {
                // Spawn moment on the blast timeline. The swarmSpawnTiming envelope is now the WHOLE mapping
                // (swarmSpawnWindow is gone): particle number `frac` (0 = first, 1 = last) → spawn life directly.
                // IsLinearTo recognises any two-point (0,0)→(1,K) curve (the default among them, K = 0.5) and takes
                // the EXACT fast path `K * i / (n-1)` VERBATIM — the pre-removal arithmetic `window * i / (n-1)`
                // with the endpoint K standing in for `window` (default endpoint 0.5 reproduces the old default
                // window 0.5), so a default/linear-shaped timing stays byte-identical (the orchestrator hash-checks
                // this; re-associating the multiply as K*(i/(n-1)) would risk ULP drift). Any other curve / MinMax /
                // Static goes through Eval, whose result IS the spawn life now — no window multiply. The Clamp01 is
                // a no-op for the default (0..0.5) so it does not perturb the byte-identical output. n ≥ 2 so
                // (n-1) ≥ 1; particle 0 lands at spawnLife 0.
                float spawnLife;
                if (layer.swarmTiming == SwarmTiming.FrameStep)
                    // FrameStep (G3): particle i spawns on frame (swarmFirstFrame + i·swarmFrameStep), mapped to the
                    // [0,1] blast life. step 0 ⇒ every particle lands on swarmFirstFrame. Gated by the swarmTiming
                    // enum, which defaults to Window, so a default swarm never enters this branch (byte-identical).
                    spawnLife = Mathf.Clamp01((layer.swarmFirstFrame + i * layer.swarmFrameStep) / (float)Mathf.Max(1, frames - 1));
                else if (n <= 1)
                    spawnLife = 0f;   // single-char Text: one particle at timeline 0 (avoids the (n-1)==0 divisions)
                else if (IsLinearTo(layer.swarmSpawnTiming, out float k))
                    spawnLife = Mathf.Clamp01(k * i / (n - 1));
                else
                    spawnLife = Mathf.Clamp01(Eval(layer.swarmSpawnTiming, i / (float)(n - 1), spec.seed, i, FldSpawnTiming));

                // SPAWN-TIME SNAPSHOT — the ENTIRE shape transform (radius, offset, rotation, pitch, yaw) is
                // evaluated at THIS particle's spawn life, NOT the current frame, so an animated transform leaves a
                // growing trail of placements rather than retroactively resizing/rotating/sliding already-placed
                // particles (the same live-vs-snapshot split Pyre's scatter makes: ringExpand is live, but
                // spawnRadius/ringStartAngle are spawn-snapshot). Radius first — both the local placement and the
                // depth normalize below need it.
                float r = Mathf.Max(0f, Eval(layer.shapeScale, spawnLife, spec.seed, i, FldScale));
                float snap = layer.shapeScaleSnap;
                if (snap > 0f) r = Mathf.Round(r / snap) * snap;

                // Even-path spacing (B1): in Path mode with swarmEvenPath, ADD an even index fraction to this
                // particle's progress so the whole string spreads evenly along the outline (swarmProgress then
                // becomes the string's shared ride). Off / non-Path ⇒ progressAdd is exactly 0, and PlaceParticle
                // skips the add, so placement is byte-identical to pre-S1.
                float progressAdd = 0f;
                if (layer.swarmEvenPath && layer.swarmSpawnMode == SwarmSpawnMode.Path && n > 1)
                    progressAdd = (i / (float)(n - 1)) * Mathf.Clamp01(layer.swarmPathSpread);

                // 1) Local placement (T2): a point on/inside the shape, radius already baked in. PlaceParticle
                //    returns it in ABSOLUTE canvas pixels (centre baked in as cx/cy); we take the local offset
                //    from that below only when a transform is actually active.
                Vector2 baseAbs = PlaceParticle(spec, layer, i, spawnLife, cx, cy, r, progressAdd);

                // Shared shape transform, each field the same spawn-time snapshot as r above. Every step is an exact
                // no-op at its default (rotation/pitch/yaw 0, offset 0), so a swarm with all transform fields at
                // defaults short-circuits to the untransformed T2 position `baseAbs` — byte-identical to pre-T3.
                float rot   = Eval(layer.shapeRotation, spawnLife, spec.seed, i, FldRotation);
                float yaw   = Eval(layer.shapeYaw,      spawnLife, spec.seed, i, FldYaw);
                float pitch = Eval(layer.shapePitch,    spawnLife, spec.seed, i, FldPitch);
                float offX  = Eval(layer.shapeOffsetX,  spawnLife, spec.seed, i, FldOffsetX);
                float offY  = Eval(layer.shapeOffsetY,  spawnLife, spec.seed, i, FldOffsetY);

                Vector2 pos;
                float zNorm;
                if (rot == 0f && yaw == 0f && pitch == 0f && offX == 0f && offY == 0f)
                {
                    // Nothing to apply → keep the exact T2 pixel position (this is what guarantees the byte-
                    // identical hash; re-deriving cx + (baseAbs - cx) would risk sub-ULP round-trip drift).
                    pos = baseAbs;
                    zNorm = 0f;
                }
                else
                {
                    // The full transform pipeline (steps 2–6) is factored into ApplyShapeTransform so
                    // ComputeSpawnTrace can reuse the IDENTICAL math. Byte-identical to the inline code it
                    // replaced: the default all-zero case is the `if` branch above and never reaches here.
                    pos = ApplyShapeTransform(baseAbs, cx, cy, r, rot, yaw, pitch, offX, offY, out zNorm);
                }

                // Per-particle facing (S1). Gated on swarmOrient != None, so a None swarm computes orientDeg 0 and
                // adds NO Eval calls (byte-identical). Outward = the screen angle from the shape centre (post-
                // transform = cx+offX/cy+offY, which is (cx,cy) in the untransformed branch) to the placement.
                // PathTangent (Path mode only) = the outline tangent at this particle's path position, rotated by
                // the same 2D shape rotation the position got (the pseudo-3D yaw/pitch tilt is intentionally NOT
                // folded into the tangent — it stays a 2D outline direction). PathTangent in Area, or a degenerate
                // (zero-length) tangent, falls back to Outward. The angle is a target MATH angle; each form folds
                // it into its own rotation (see DrawParticle).
                float orientDeg = 0f;
                if (layer.swarmOrient != SwarmOrient.None)
                {
                    float ctrX = cx + offX, ctrY = cy + offY;
                    bool wantTangent = layer.swarmOrient == SwarmOrient.PathTangent
                                       && layer.swarmSpawnMode == SwarmSpawnMode.Path;
                    bool got = false;
                    if (wantTangent)
                    {
                        Vector2 tl = PathTangentLocal(spec, layer, i, spawnLife, r, progressAdd);
                        if (rot != 0f)
                        {
                            float a = rot * Mathf.Deg2Rad, cc = Mathf.Cos(a), ss = Mathf.Sin(a);
                            tl = new Vector2(tl.x * cc - tl.y * ss, tl.x * ss + tl.y * cc);
                        }
                        if (tl.sqrMagnitude > 1e-8f) { orientDeg = Mathf.Atan2(tl.y, tl.x) * Mathf.Rad2Deg; got = true; }
                    }
                    if (!got)
                    {
                        float dxo = pos.x - ctrX, dyo = pos.y - ctrY;
                        if (dxo * dxo + dyo * dyo > 1e-8f) orientDeg = Mathf.Atan2(dyo, dxo) * Mathf.Rad2Deg;
                    }
                }

                into.Add(new SpawnPoint { spawnLife = spawnLife, pos = pos, zNorm = zNorm, orientDeg = orientDeg });
            }
        }

        /// The shared shape-transform math — ComputeSpawns' steps 2–6, factored out VERBATIM so ComputeSpawnTrace
        /// reuses the exact same pipeline. Given an absolute placement `baseAbs` (the shape radius already baked in)
        /// and the spawn-time-snapshot transform fields, it works in shape-local coords: 2D-rotates the point,
        /// pseudo-3D tilts it (yaw about the vertical axis THEN pitch about the horizontal), depth-normalizes z
        /// against `r`, applies the perspective spread, and re-offsets to screen space. `out zNorm` is the pseudo-3D
        /// depth (+1 nearest, −1 farthest, 0 flat). Every branch is guarded by a `!= 0f` so a partial transform only
        /// pays for the axes it uses. This changed no float operation relative to the inline code (byte-identical).
        static Vector2 ApplyShapeTransform(Vector2 baseAbs, float cx, float cy, float r,
                                           float rot, float yaw, float pitch, float offX, float offY, out float zNorm)
        {
            // Work in shape-local coords relative to the centre; the radius is already inside baseAbs.
            float x = baseAbs.x - cx, y = baseAbs.y - cy, z = 0f;

            // 2) 2D rotation of the local point, counter-clockwise (y-up), degrees.
            if (rot != 0f)
            {
                float a = rot * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
                float nx = x * c - y * s;
                float ny = x * s + y * c;
                x = nx; y = ny;
            }

            // 3) Pseudo-3D tilt of (x, y, 0). YAW first — rotate about the vertical y axis in the (x,z)
            //    plane; z gains x·sin(yaw), so yaw 90° sends every point to x≈0 (shape → vertical line).
            if (yaw != 0f)
            {
                float a = yaw * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
                float nx = x * c - z * s;
                float nz = x * s + z * c;
                x = nx; z = nz;
            }
            //    THEN PITCH — rotate about the horizontal x axis in the (y,z) plane; z gains y·sin(pitch),
            //    so pitch 90° sends every point to y≈0 (shape → horizontal line).
            if (pitch != 0f)
            {
                float a = pitch * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
                float ny = y * c - z * s;
                float nz = y * s + z * c;
                y = ny; z = nz;
            }

            // 4) Depth normalize against the (snapped) radius: +1 nearest the viewer, -1 farthest, 0 flat.
            zNorm = Mathf.Clamp(z / Mathf.Max(1e-3f, r), -1f, 1f);
            // 5) Perspective feel: nearer parts spread outward slightly (persp == 1 exactly at zNorm 0).
            float persp = 1f + 0.25f * zNorm;
            // 6) Screen-space offset of the whole shape, applied last.
            return new Vector2(cx + offX + x * persp, cy + offY + y * persp);
        }

        /// Compute the CANONICAL, index-free spine of the swarm's spawn point across the whole timeline — the
        /// objective path the spawn POSITION sweeps as spawn-life runs 0→1 (Part B's second visualisation, distinct
        /// from the per-particle spawn dots). Fills `into` (cleared first) with `samples` absolute canvas-pixel
        /// points, evenly spaced in t; the caller sizes `samples` (the overlay uses ≈4×frameCount clamped 64..512).
        ///
        /// It mirrors ComputeSpawns' position pipeline at each t — radius snapshot → placement → shared transform —
        /// but is deliberately CANONICAL where ComputeSpawns is per-index/seeded:
        ///   • every field evaluates through EvalCanonical (MinMax → its MIDPOINT), never a seeded per-particle draw;
        ///   • NO per-index even-path offset (swarmEvenPath is ignored — the trace is the shared ride, not a string);
        ///   • Area mode traces the transformed shape CENTRE (local 0,0), i.e. the pure offset trajectory, rather
        ///     than a random interior point.
        /// So on a plain Static/Curve transform the trace lands exactly on the real placements; with any MinMax or
        /// per-index effect it is the SPINE the dots scatter around — the dots stay the actual placements. Pure and
        /// deterministic (no random draw ⇒ no seed needed); empty when the swarm is off. Reuses ApplyShapeTransform,
        /// SideCount and PolyVertex; the outline arithmetic mirrors PlaceParticle with canonical progress (that path
        /// is left untouched so the real per-particle placement stays byte-identical).
        public static void ComputeSpawnTrace(PyrePlusSpec spec, PyrePlusLayer layer, int samples, List<Vector2> into)
        {
            if (into == null) return;
            into.Clear();
            if (spec == null || layer == null || !layer.swarmEnabled) return;
            samples = Mathf.Max(2, samples);

            float cx = spec.Width * 0.5f;
            float cy = spec.Height * 0.5f;
            var kind = layer.swarmShapeKind;
            bool path = layer.swarmSpawnMode == SwarmSpawnMode.Path;

            for (int si = 0; si < samples; si++)
            {
                float t = si / (float)(samples - 1);   // spawn-life 0→1, evenly spaced

                // Radius: the same spawn-time-snapshot radius, canonical at t, snapped exactly as ComputeSpawns.
                float r = Mathf.Max(0f, EvalCanonical(layer.shapeScale, t));
                float snap = layer.shapeScaleSnap;
                if (snap > 0f) r = Mathf.Round(r / snap) * snap;

                // Local placement, index-free. Area → the shape CENTRE (offset trajectory). Path → the outline
                // point at the canonical progress; Custom clamps, closed shapes wrap (p − floor p) exactly as
                // PlaceParticle. Uses EvalCanonical for progress/Custom (no seeded draw).
                Vector2 baseAbs;
                if (!path)
                {
                    baseAbs = new Vector2(cx, cy);
                }
                else
                {
                    float pRaw = EvalCanonical(layer.swarmProgress, t);
                    if (kind == SwarmShapeKind.Custom)
                    {
                        float pc = Mathf.Clamp01(pRaw);
                        baseAbs = new Vector2(cx + EvalCanonical(layer.swarmCustomX, pc),
                                              cy + EvalCanonical(layer.swarmCustomY, pc));
                    }
                    else if (kind == SwarmShapeKind.Circle)
                    {
                        float p = pRaw - Mathf.Floor(pRaw);
                        float ang = Mathf.PI / 2f + 2f * Mathf.PI * p;
                        baseAbs = new Vector2(cx + r * Mathf.Cos(ang), cy + r * Mathf.Sin(ang));
                    }
                    else
                    {
                        int n = SideCount(kind);
                        if (n < 3) baseAbs = new Vector2(cx, cy);
                        else
                        {
                            float p = pRaw - Mathf.Floor(pRaw);
                            float tt = p * n;
                            int k = (int)tt;
                            float frac = tt - k;
                            if (k >= n) { k = n - 1; frac = 1f; }
                            Vector2 a = PolyVertex(cx, cy, r, k, n);
                            Vector2 b = PolyVertex(cx, cy, r, k + 1, n);
                            baseAbs = Vector2.Lerp(a, b, frac);
                        }
                    }
                }

                // Shared shape transform (canonical snapshot at t) through the SAME pipeline the render uses.
                float rot   = EvalCanonical(layer.shapeRotation, t);
                float yaw   = EvalCanonical(layer.shapeYaw, t);
                float pitch = EvalCanonical(layer.shapePitch, t);
                float offX  = EvalCanonical(layer.shapeOffsetX, t);
                float offY  = EvalCanonical(layer.shapeOffsetY, t);
                Vector2 pos = ApplyShapeTransform(baseAbs, cx, cy, r, rot, yaw, pitch, offX, offY, out _);

                // FIX 3 — the whole cloud spins LIVE (swarmTurn/Tilt/Roll). The trace is the objective path the
                // spawn point sweeps over the timeline, so at sample t it must ALSO carry the swarm spin evaluated
                // at t — the traced spine itself sweeps as the cloud turns. CANONICAL (EvalCanonical → MinMax
                // midpoint, no seed), matching the rest of this method; a Static/Curve spin coincides exactly with
                // the render's per-frame Eval. Reuses ApplyShapeTransform with rot = Roll, yaw = Turn, pitch = Tilt
                // around the shape centre (the SAME mapping RenderSwarm / ApplySwarmSpin use), depth-normalized
                // against the point's radial distance. ALL-ZERO spin is an EXACT no-op, so a non-spinning swarm's
                // trace is unchanged.
                float sTurn = EvalCanonical(layer.swarmTurn, t);
                float sTilt = EvalCanonical(layer.swarmTilt, t);
                float sRoll = EvalCanonical(layer.swarmRoll, t);
                if (sTurn != 0f || sTilt != 0f || sRoll != 0f)
                    pos = ApplyShapeTransform(pos, cx, cy, Vector2.Distance(pos, new Vector2(cx, cy)),
                                              sRoll, sTurn, sTilt, 0f, 0f, out _);
                // Slice 0 — the trace also carries the LIVE whole-cloud scale, evaluated CANONICALLY at t (like the
                // spin above) and applied AFTER it (centre + scale·spin(offset)), so the spine tracks the scaled
                // cloud. Scale 1 (the default) is an EXACT no-op, so a non-scaling swarm's trace is unchanged.
                float sScale = EvalCanonical(layer.swarmScale, t);
                if (sScale != 1f)
                    pos = new Vector2(cx + (pos.x - cx) * sScale, cy + (pos.y - cy) * sScale);
                into.Add(pos);
            }
        }

        /// Canonical, INDEX-FREE evaluation of a transform/placement ZUIValue, for ComputeSpawnTrace only: Static →
        /// the value, MinMax → the MIDPOINT (no seeded per-particle draw), Curve → the envelope at t. The runtime
        /// twin of the editor overlay's EvalField, and it matches Eval's Static/Curve branches exactly — so on a
        /// Static or Curve field the trace coincides with the real placements; only MinMax differs, by design.
        static float EvalCanonical(ZUIValue v, float t)
        {
            if (v == null) return 0f;
            switch (v.mode)
            {
                case ZUIValue.Mode.Static: return v.staticValue;
                case ZUIValue.Mode.MinMax: return (v.min + v.max) * 0.5f;
                // Every envelope-shaped mode goes through the value's OWN normalized evaluators, so the
                // canonical trace matches what the control draws. Steps had no case here at all and fell to
                // the static value, so a Steps-authored field traced flat while the real placements stepped;
                // and the old raw ZUIEnvelopeEvaluator call for Curve silently dropped corner smoothness.
                case ZUIValue.Mode.Curve: return v.EvaluateCurveAtNorm(Mathf.Clamp01(t));
                case ZUIValue.Mode.Steps: return v.EvaluateStepsAtNorm(Mathf.Clamp01(t));
                case ZUIValue.Mode.Oscillation: return v.EvaluateOscillationAtNorm(Mathf.Clamp01(t));
                default: return v.staticValue;
            }
        }

        /// Place one swarm particle in absolute canvas-pixel coords, given its spawn-time-snapshot shape radius
        /// `r` (circumradius) and index `i`. THE single source of placement truth — RenderSwarm and T5's preview
        /// overlay both get every dot from ComputeSpawns, i.e. from here. Pure/deterministic: any randomness is a
        /// System.Random keyed via Hash by (seed, i, field), stable across frames, never UnityEngine.Random.
        ///   Area — a uniform-by-AREA random point INSIDE the shape (disc for Circle/Custom, fan-triangulated
        ///          regular polygon otherwise).
        ///   Path — a point ON the shape's outline, positioned by progress (see below).
        /// Geometry conventions: regular N-gon has vertex 0 at the TOP (angle +90°), vertices counter-clockwise;
        /// side count Triangle 3 / Square 4 / Pentagon 5 / Hexagon 6.
        static Vector2 PlaceParticle(PyrePlusSpec spec, PyrePlusLayer layer, int i, float spawnLife, float cx, float cy, float r,
                                     float progressAdd = 0f)
        {
            var kind = layer.swarmShapeKind;

            // Line (slice 3 — Bars row): particle i sits at fraction f = i/(n-1) along a straight HORIZONTAL segment
            // through the shape centre, endpoints at ±r (so it spans the same extent a Circle of radius r would).
            // Independent of Area/Path — a Line is 1-D, so it ignores swarmSpawnMode/progress entirely. The default
            // local axis is +x (horizontal), perpendicular to the Streak's default up/+y forward, so a row of Streaks
            // on a Line reads as Pyre's row of upright bars; the shared shape transform (shapeRotation/pitch/yaw) then
            // rotates the whole row, making shapeRotation the row angle. `n` mirrors ComputeSpawns' count (Text →
            // string length, else Max(2, swarmCount)); n≤1 pins to the centre. Byte-identity: a brand-new enum value
            // no existing spec can hold, so this branch never fires for old data.
            if (kind == SwarmShapeKind.Line)
            {
                int nl = layer.shapeForm == ShapeForm.Text
                    ? Mathf.Max(1, (layer.textString ?? "").Length)
                    : Mathf.Max(2, layer.swarmCount);
                float f = nl > 1 ? i / (float)(nl - 1) : 0f;   // 0 at the first particle … 1 at the last
                return new Vector2(cx + (f * 2f - 1f) * r, cy);   // −r … +r along +x, through the centre
            }

            if (layer.swarmSpawnMode == SwarmSpawnMode.Area)
            {
                // Custom is Path-only (the UI enforces it), so Area+Custom falls back to a disc — as does Circle.
                if (kind == SwarmShapeKind.Circle || kind == SwarmShapeKind.Custom)
                {
                    Vector2 off = SampleDisc(r, spec.seed, i);
                    return new Vector2(cx + off.x, cy + off.y);
                }
                return SamplePolygonArea(cx, cy, r, SideCount(kind), spec.seed, i);
            }

            // Path mode. Progress is a SPAWN-TIME SNAPSHOT: sample swarmProgress at THIS particle's own spawn
            // life, NOT the current frame — so a Curve/rewinding progress leaves a trail of placements instead of
            // retroactively sliding already-placed particles. A MinMax swarmProgress falls out of Eval as a
            // per-particle random point along the path (no special-casing). Path mode uses NO disc randomness of
            // its own — the only randomness is whatever mode swarmProgress itself is in.
            float pRaw = Eval(layer.swarmProgress, spawnLife, spec.seed, i, FldProgress);
            if (progressAdd != 0f) pRaw += progressAdd;   // even-path (B1) offset; 0 ⇒ skipped ⇒ byte-identical

            if (kind == SwarmShapeKind.Custom)
            {
                // Custom is an OPEN polyline (it has two distinct ends), so progress CLAMPS — p<0 pins to the
                // start, p>1 pins to the end. The two envelopes ARE the path: x(p), y(p) as canvas-pixel offsets
                // from the centre, sampled at progress p. p=0 is the path's start, p=1 its end.
                float pc = Mathf.Clamp01(pRaw);
                float ox = Eval(layer.swarmCustomX, pc, spec.seed, i, FldCustomX);
                float oy = Eval(layer.swarmCustomY, pc, spec.seed, i, FldCustomY);
                return new Vector2(cx + ox, cy + oy);
            }

            // Closed shapes (Circle + regular N-gons) WRAP instead of clamping, so the spawn point can run around
            // the outline more than once: p above 1 means extra laps. The fractional part p - floor(p) keeps p in
            // [0,1); it sends every integer to the path start (0) and folds negatives up too (floor(-0.3) = -1 →
            // 0.7). BYTE-IDENTITY: for p in [0,1) floor(p) is exactly 0, so p is unchanged — identical to the old
            // Clamp01(p); at p == 1 exactly wrap gives 0, the SAME position the old Clamp01(1)=1 produced (Circle:
            // angle π/2, the top; N-gon: vertex 0, the top). So every progress value in [0,1] renders exactly as
            // before (orchestrator hash-verified), and only progress OUTSIDE [0,1] — previously unreachable under
            // Clamp01 — newly maps onto further laps.
            float p = pRaw - Mathf.Floor(pRaw);

            if (kind == SwarmShapeKind.Circle)
            {
                // Counter-clockwise from the top (+90°): p=0 at the top, p=1 once around back to the top.
                float ang = Mathf.PI / 2f + 2f * Mathf.PI * p;
                return new Vector2(cx + r * Mathf.Cos(ang), cy + r * Mathf.Sin(ang));
            }

            // Regular N-gon: walk the closed perimeter by arc length. Every side is the same length, so p·N is
            // directly the (segment index + fraction) arc-length parameter — no need to accumulate side lengths.
            // p=0 is vertex 0 (top), p=1 wraps back to vertex 0.
            int n = SideCount(kind);
            float t = p * n;
            int k = (int)t;
            float frac = t - k;
            if (k >= n) { k = n - 1; frac = 1f; }        // defensive: wrapped p is < 1 so t < n and this can't fire
            Vector2 a = PolyVertex(cx, cy, r, k, n);
            Vector2 b = PolyVertex(cx, cy, r, k + 1, n); // k+1 may equal n; trig is periodic → vertex 0
            return Vector2.Lerp(a, b, frac);
        }

        /// The outline TANGENT at particle i's path position, in shape-LOCAL space (centre at origin, before the
        /// shared transform), for the PathTangent orient mode (Path mode only). Direction only — magnitude is
        /// ignored by the caller (it takes the angle). Re-derives the same progress PlaceParticle uses (Eval is
        /// deterministic, so re-evaluating a MinMax progress yields the identical draw — no stream corruption):
        ///   Circle — the CCW tangent of the parameterized circle at progress p.
        ///   N-gon  — the current side's direction (vertex k → k+1), by the same arc-length p·n parameterization.
        ///   Custom — a small central finite difference of the paired X/Y envelopes about the clamped progress.
        static Vector2 PathTangentLocal(PyrePlusSpec spec, PyrePlusLayer layer, int i, float spawnLife, float r, float progressAdd)
        {
            var kind = layer.swarmShapeKind;
            float pRaw = Eval(layer.swarmProgress, spawnLife, spec.seed, i, FldProgress);
            if (progressAdd != 0f) pRaw += progressAdd;

            if (kind == SwarmShapeKind.Custom)
            {
                float pc = Mathf.Clamp01(pRaw);
                const float dp = 0.01f;
                float pa = Mathf.Clamp01(pc - dp), pb = Mathf.Clamp01(pc + dp);
                float x0 = Eval(layer.swarmCustomX, pa, spec.seed, i, FldCustomX);
                float x1 = Eval(layer.swarmCustomX, pb, spec.seed, i, FldCustomX);
                float y0 = Eval(layer.swarmCustomY, pa, spec.seed, i, FldCustomY);
                float y1 = Eval(layer.swarmCustomY, pb, spec.seed, i, FldCustomY);
                return new Vector2(x1 - x0, y1 - y0);
            }

            float p = pRaw - Mathf.Floor(pRaw);   // wrap on closed shapes, exactly as PlaceParticle

            if (kind == SwarmShapeKind.Circle)
            {
                // pos(p) = r·(cos ang, sin ang), ang = π/2 + 2π p → tangent ∝ (−sin ang, cos ang) (CCW).
                float ang = Mathf.PI / 2f + 2f * Mathf.PI * p;
                return new Vector2(-Mathf.Sin(ang), Mathf.Cos(ang));
            }

            int n = SideCount(kind);
            if (n <= 0) return Vector2.zero;
            float t = p * n;
            int k = (int)t;
            if (k >= n) k = n - 1;
            Vector2 va = PolyVertex(0f, 0f, r, k, n);       // local (centre at origin) so the direction has no offset
            Vector2 vb = PolyVertex(0f, 0f, r, k + 1, n);
            return vb - va;
        }

        /// Side count for a regular-polygon shape kind (0 for Circle/Custom, which are not regular polygons).
        static int SideCount(SwarmShapeKind kind)
        {
            switch (kind)
            {
                case SwarmShapeKind.Triangle: return 3;
                case SwarmShapeKind.Square:   return 4;
                case SwarmShapeKind.Pentagon: return 5;
                case SwarmShapeKind.Hexagon:  return 6;
                default:                      return 0;
            }
        }

        /// Absolute canvas-pixel position of vertex `k` of a regular `n`-gon (circumradius `r`, centre cx/cy).
        /// Vertex 0 is at the TOP (angle +90°) and vertices advance counter-clockwise. `k` may exceed n-1; the
        /// trig is periodic so k and k%n coincide (used when lerping the last side back to vertex 0).
        static Vector2 PolyVertex(float cx, float cy, float r, int k, int n)
        {
            float ang = Mathf.PI / 2f + 2f * Mathf.PI * k / n;
            return new Vector2(cx + r * Mathf.Cos(ang), cy + r * Mathf.Sin(ang));
        }

        /// A uniform-by-AREA random point inside a regular `n`-gon (circumradius `r`, centre cx/cy). Fan-
        /// triangulates the polygon into n congruent triangles sharing the centre and draws THREE independent
        /// well-avalanched uniforms (HashUniform with distinct salts): u0 picks the triangle (congruent ⇒ uniform by
        /// area), u1/u2 sample uniformly inside it via the standard sqrt barycentric trick. Bug fix — the pre-fix
        /// draw built ONE fresh System.Random keyed by the RAW (un-avalanched) Hash and took its first three
        /// NextDouble()s; neighbouring particleIndex seeds produced near-identical first outputs, clustering the
        /// samples into star/spoke rays. HashUniform's fmix32 finalizer decorrelates neighbours ⇒ true uniform-by-
        /// area coverage. Still fully seeded/deterministic ⇒ per-particle stable across frames + preview/bake/runtime.
        static Vector2 SamplePolygonArea(float cx, float cy, float r, int n, int seed, int particleIndex)
        {
            float u0 = HashUniform(seed, particleIndex, FldPlacement, _layerSalt);
            float u1 = HashUniform(seed, particleIndex, FldPlacement, _layerSalt ^ 0x68BC21EB);
            float u2 = HashUniform(seed, particleIndex, FldPlacement, _layerSalt ^ 0x2545F491);

            int tri = Mathf.Clamp((int)(u0 * n), 0, n - 1);
            Vector2 A = PolyVertex(cx, cy, r, tri, n);       // rim vertices of the chosen fan triangle
            Vector2 B = PolyVertex(cx, cy, r, tri + 1, n);   // C = centre (cx, cy)

            float s = Mathf.Sqrt(u1);
            float wC = 1f - s;                 // P = C·(1-s) + A·(s·(1-u2)) + B·(s·u2)
            float wA = s * (1f - u2);
            float wB = s * u2;
            return new Vector2(cx * wC + A.x * wA + B.x * wB,
                               cy * wC + A.y * wA + B.y * wB);
        }

        /// A uniformly-distributed random point inside a disc of the given radius — sqrt-distributed radius so
        /// points spread evenly by AREA (not clumped at the centre), angle uniform. Draws TWO independent well-
        /// avalanched uniforms (HashUniform with distinct salts) keyed on (seed, particleIndex, FldPlacement) so the
        /// draw is stable across frames and identical in preview/bake/runtime. Bug fix — the pre-fix draw built ONE
        /// fresh System.Random from the RAW (un-avalanched) Hash and took its first two NextDouble()s; neighbouring
        /// particleIndex seeds gave near-identical first outputs, so the angle draw clustered and the swarm read as
        /// a star/spokes instead of a filled disc. HashUniform's fmix32 finalizer decorrelates neighbouring indices.
        static Vector2 SampleDisc(float radius, int seed, int particleIndex)
        {
            float u1 = HashUniform(seed, particleIndex, FldPlacement, _layerSalt);
            float u2 = HashUniform(seed, particleIndex, FldPlacement, _layerSalt ^ 0x68BC21EB);
            float rr = radius * Mathf.Sqrt(u1);
            float ang = u2 * 2f * Mathf.PI;
            return new Vector2(rr * Mathf.Cos(ang), rr * Mathf.Sin(ang));
        }

        // sizeMul/brightMul default to 1 → the swarm-off single-particle call (and any untilted swarm particle,
        // whose zNorm is 0) draws byte-identical to the pre-T3 path: radius unchanged, colour passed through raw.
        // `mods` carries this frame's geometry+pixel modifiers (ModSet.Empty ⇒ the fast path below, byte-identical
        // to pre-T6). Both the single-particle and swarm callers share this one method so warps apply to either.
        // orientDeg (S1) is the swarm's per-particle facing (0 for the swarm-off / orient-None paths → every
        // rotation fold below is guarded to a no-op, keeping those paths byte-identical). It ADDS to whichever
        // rotation each form already has: Streak forward, Disc/Crescent/Sparkle/Sprite spin, Text/solid roll.
        // lenByIndex (slice 3) is the STREAK-only length-only index multiplier (see RenderSwarm). It defaults to 1 —
        // the swarm-off single-particle call and every non-Streak form leave it untouched, and DrawStreakBody's
        // len·1 is bit-exact — so all those paths stay byte-identical.
        static void DrawParticle(Color32[] buf, int W, int H, float cx, float cy, float life,
                                 PyrePlusSpec spec, PyrePlusLayer layer, int particleIndex, in ModSet mods, float phase, int frameIndex,
                                 float sizeMul = 1f, float brightMul = 1f, float orientDeg = 0f, float lenByIndex = 1f)
        {
            // Text form: this particle renders ITS OWN character (index = particleIndex) as an extruded SDF glyph
            // at (cx, cy). This is the SWARM path (each swarm particle draws one letter at its swarm position); the
            // swarm-OFF line is laid out separately by RenderTextLine. When no readable font exists (_textReady is
            // false) we fall THROUGH to the Disc raster below as the per-particle fallback — so travel/size/alpha
            // are left for the Disc code to evaluate and cx/cy stay untouched here in that case.
            if (layer.shapeForm == ShapeForm.Text)
            {
                if (_textReady)
                {
                    string ts = layer.textString ?? "";
                    if (ts.Length == 0) return;
                    char ch = ts[Mathf.Clamp(particleIndex, 0, ts.Length - 1)];
                    float szc = Mathf.Max(0f, Eval(layer.size, life, spec.seed, particleIndex, FldSize)) * sizeMul;
                    // Shared per-particle travel (T7) — same guard/keys as every other form (no-op at Static 0).
                    if (!(IsStaticZero(layer.particlePathX) && IsStaticZero(layer.particlePathY)))
                    {
                        cx += Eval(layer.particlePathX, life, spec.seed, particleIndex, FldPathX);
                        cy += Eval(layer.particlePathY, life, spec.seed, particleIndex, FldPathY);
                    }
                    // Swarm mode has no line layout, so TextGradient degrades to PerCharStep (index fraction):
                    // pass lineMode=false, x0/span 0 (unused). orientDeg rolls each letter about Z to follow the path.
                    DrawTextChar(buf, W, H, cx, cy, szc, life, spec, layer, particleIndex, ch, particleIndex,
                                 Mathf.Max(1, ts.Length), 0f, 0f, false, mods, frameIndex, brightMul, orientDeg);
                    return;
                }
                // else: no readable font → fall through to the Disc raster (the documented per-particle fallback).
            }

            // The true-3D facet SOLIDS (Gem + Box/Pyramid/Can) branch FIRST, before any Disc arithmetic below, so
            // the Disc path stays textually untouched and its output byte-identical (the orchestrator hash-gates
            // Disc). All four share DrawFacetSolid — same rotation, lighting, edge lines and glows — differing only
            // in their model verts / faces / which edges are lines. `life` here IS the particle's own life clock in
            // both callers (blast life for the single particle, `own` for a swarm one).
            if (layer.shapeForm == ShapeForm.Gem || layer.shapeForm == ShapeForm.Box ||
                layer.shapeForm == ShapeForm.Pyramid || layer.shapeForm == ShapeForm.Can)
            {
                DrawFacetSolid(buf, W, H, cx, cy, life, spec, layer, particleIndex, mods, frameIndex, sizeMul, brightMul, orientDeg);
                return;
            }
            // Orb + Ring — the ANALYTIC true-3D forms (a sphere and a flat annulus). They reuse the SAME per-pixel
            // point-light + Blinn-Phong lighting, hard edge lines and halo/inner glows as DrawFacetSolid, but drive
            // them from analytic geometry instead of facets (see DrawOrb / DrawRing). Branch before the Disc
            // arithmetic so the Disc path stays byte-identical, exactly like the facet-solid branch above.
            if (layer.shapeForm == ShapeForm.Orb)
            {
                DrawOrb(buf, W, H, cx, cy, life, spec, layer, particleIndex, mods, frameIndex, sizeMul, brightMul, orientDeg);
                return;
            }
            if (layer.shapeForm == ShapeForm.Ring)
            {
                // Ring intentionally ignores orientDeg: its roll (gemRoll) is geometrically inert for the symmetric
                // annulus (Turn + Tilt already shape its ellipse), and DrawRing applies no roll at all.
                DrawRing(buf, W, H, cx, cy, life, spec, layer, particleIndex, mods, frameIndex, sizeMul, brightMul);
                return;
            }
            // Streak — a root-anchored comet-tail capsule with its OWN length/width envelopes (NOT `size`). Branch
            // before the Disc arithmetic so the Disc path stays byte-identical, like the solid/analytic forms above.
            if (layer.shapeForm == ShapeForm.Streak)
            {
                DrawStreakBody(buf, W, H, cx, cy, life, spec, layer, particleIndex, mods, phase, frameIndex,
                               sizeMul, brightMul, orientDeg, lenByIndex);
                return;
            }

            float radius = Mathf.Max(0f, Eval(layer.size, life, spec.seed, particleIndex, FldSize));
            radius *= sizeMul;                    // depth size shading (exact no-op at sizeMul == 1)
            if (radius <= 0.01f) return;
            float alpha = Mathf.Clamp01(Eval(layer.alpha, life, spec.seed, particleIndex, FldAlpha));
            if (alpha <= 0.002f) return;

            // Per-particle travel (T7): the particle's OWN path after birth — canvas-pixel offsets ADDED to its
            // centre on its own life clock (this method's `life` IS that own clock in both callers: the single
            // particle's own life == the blast life, and a swarm particle's is `own`). Applied before BOTH the fast
            // raster and the modifier raster below, so the whole disc — bounds and warp context included — travels
            // as a unit. Guarded to an exact no-op when BOTH values are Static 0, keeping a default asset (and its
            // fast-path bounds/arith) byte-identical; a Curve/MinMax always evaluates.
            if (!(IsStaticZero(layer.particlePathX) && IsStaticZero(layer.particlePathY)))
            {
                cx += Eval(layer.particlePathX, life, spec.seed, particleIndex, FldPathX);
                cy += Eval(layer.particlePathY, life, spec.seed, particleIndex, FldPathY);
            }

            // Shape fill (Part A). Hoisted at the particle centre (0,0). For the default OverLife fill (and any
            // Solid fill) this is a constant the whole raster reuses — byte-identical to the old colorOverLife path
            // (OverLife evaluates as gradient.Evaluate(life), the exact old call). A SPATIAL fill (Linear/Radial/
            // Noise) instead recomputes per pixel at the pixel's local (dx/radius, dy/radius) inside each loop below.
            var fill = layer.shapeFill;
            bool fillSpatial = IsSpatialFill(fill);
            Color col = fill != null ? fill.Evaluate(life, 0f, 0f) : Color.white;
            // Depth brightness shading: scale RGB (alpha untouched) and clamp each channel to [0,1]. Guarded so
            // brightMul == 1 passes the colour through byte-for-byte (the swarm-off and untilted-swarm paths).
            float cr = col.r, cg = col.g, cb = col.b;
            if (brightMul != 1f)
            {
                cr = Mathf.Clamp01(cr * brightMul);
                cg = Mathf.Clamp01(cg * brightMul);
                cb = Mathf.Clamp01(cb * brightMul);
            }
            // Soft rim: alpha ramps from `soft`·radius out to the edge. 0 softness = a hard pixel disc. Edge is now
            // animatable over life (#12): a null companion falls back to the legacy float ⇒ byte-identical.
            float soft = Mathf.Clamp01(layer.edgeSoftnessAnim != null
                ? Eval(layer.edgeSoftnessAnim, life, spec.seed, particleIndex, FldEdgeSoftness) : layer.edgeSoftness);
            float inner = radius * (1f - soft);

            // ── other STATELESS forms (G2) — branch BEFORE the Disc raster so the Disc/Gem paths below stay
            //    textually untouched (both are hash-gated). Each shares the contract Disc uses: radius from
            //    `size`·sizeMul, alpha envelope × gradient at own life, brightMul on RGB, the travel offset
            //    already folded into cx/cy above, and spin + geometry warps folded per pixel exactly as Disc's
            //    modifier path does. Sprite alone can decline (null / non-readable texture) and fall THROUGH to
            //    the Disc raster as its fallback. ──
            if (layer.shapeForm == ShapeForm.Crescent)
            {
                DrawCrescentBody(buf, W, H, cx, cy, radius, alpha, col, cr, cg, cb, soft, inner,
                                 spec, layer, particleIndex, mods, phase, frameIndex, life, brightMul, orientDeg);
                return;
            }
            if (layer.shapeForm == ShapeForm.Star)
            {
                DrawStarBody(buf, W, H, cx, cy, radius, alpha, col, cr, cg, cb, soft,
                             spec, layer, particleIndex, mods, phase, frameIndex, life, brightMul, orientDeg);
                return;
            }
            if (layer.shapeForm == ShapeForm.Polygon)
            {
                DrawPolygonBody(buf, W, H, cx, cy, radius, alpha, col, cr, cg, cb, soft,
                                spec, layer, particleIndex, mods, phase, frameIndex, life, brightMul, orientDeg);
                return;
            }
            if (layer.shapeForm == ShapeForm.Sparkle)
            {
                DrawSparkleBody(buf, W, H, cx, cy, radius, alpha, col, cr, cg, cb,
                                spec, layer, particleIndex, mods, phase, frameIndex, life, brightMul, orientDeg);
                return;
            }
            if (layer.shapeForm == ShapeForm.Sprite)
            {
                // A null spriteImage, or a texture without Read/Write, can't be sampled — DrawSpriteBody returns
                // false and we FALL THROUGH to the Disc raster below as the fallback (the UI's picker tooltip
                // warns the texture must have Read/Write enabled).
                if (DrawSpriteBody(buf, W, H, cx, cy, radius, alpha, col, brightMul,
                                   spec, layer, particleIndex, mods, phase, frameIndex, life, orientDeg))
                    return;
            }

            // ── fast path: no modifiers → the pre-T6 raster, kept verbatim so the no-modifier hash is byte-
            //    identical (do NOT refactor this loop's arithmetic). The ONE addition (T7 spin, below) is fully
            //    guarded to a no-op for every solid fill and every Static-0 spin, so those stay byte-identical. ──
            if (!mods.Any)
            {
                // Bug fix — a textured/spatial Disc must SPIN. A SOLID disc is radially symmetric so particleSpin is
                // invisible and the pre-T7 raster is kept verbatim (byte-identical); a SPATIAL/TEXTURED fill (Dots,
                // Linear, Radial, …) is NOT symmetric, so — like the modifier path (:~2456) — its local offset is
                // rotated by −spin before the distance/edge/EvalFill so the pattern turns WITH the particle. Computed
                // ONLY for a spatial fill, and rotation applied ONLY when spin != 0: a solid fill, and any Static-0
                // spin, feed the pre-T7 (dx,dy) verbatim, so every default / solid / spin-0 disc hashes identically.
                // orientDeg is intentionally NOT folded here (unlike the modifier path) so a spin=Static-0 swarm disc
                // — oriented or not — is guaranteed byte-identical; per-particle spin is the reported bug this fixes.
                // Bounds need no change: rotation preserves the offset length, so a symmetric disc's extent is intact.
                float fSpin = 0f;
                bool fDoSpin = false;
                float fCos = 1f, fSin = 0f;
                if (fillSpatial)
                {
                    fSpin = Eval(layer.particleSpin, life, spec.seed, particleIndex, FldSpin);
                    fDoSpin = fSpin != 0f;
                    if (fDoSpin) { float sa = -fSpin * Mathf.Deg2Rad; fCos = Mathf.Cos(sa); fSin = Mathf.Sin(sa); }
                }
                int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - radius));
                int x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + radius));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - radius));
                int y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + radius));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                        if (fDoSpin)   // rotate the local offset by −spin so a spatial/textured fill turns with the particle (guarded: solid fills & Static-0 spin skip this ⇒ byte-identical)
                        {
                            float rx = dx * fCos - dy * fSin;
                            float ry = dx * fSin + dy * fCos;
                            dx = rx; dy = ry;
                        }
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        if (d > radius) continue;
                        float edge = d <= inner ? 1f : 1f - Mathf.InverseLerp(inner, radius, d);
                        if (fillSpatial)   // spatial fill → this pixel's colour at its (spun) local (u,v) or canvas-anchored (Fixed); Solid/OverLife never enter here (byte-identical)
                        {
                            col = EvalFill(fill, life, dx / radius, dy / radius, x, y, W, H);
                            cr = col.r; cg = col.g; cb = col.b;
                            if (brightMul != 1f) { cr = Mathf.Clamp01(cr * brightMul); cg = Mathf.Clamp01(cg * brightMul); cb = Mathf.Clamp01(cb * brightMul); }
                        }
                        float a = alpha * col.a * edge;
                        if (a <= 0.002f) continue;
                        Over(buf, y * W + x, cr, cg, cb, a);
                    }
                return;
            }

            // ── modifier path: fold each candidate pixel's position through the GeometryModifiers (bending the
            //    disc) BEFORE the distance/edge test, then run the PixelModifiers on each lit pixel before it
            //    composites. Mirrors BlastRenderer.RasterShape's per-pixel loop. ──
            bool anyGeo = mods.AnyGeo;
            float ccx = W * 0.5f, ccy = H * 0.5f;             // canvas centre — the frame origin the warps fold around
            Vector2 c = new Vector2(cx - ccx, cy - ccy);      // this particle's centre AS AN OFFSET from the canvas centre (GeoCtx.center convention)
            var ctx = new GeoCtx(ccx, ccy, c, radius);        // hHalf/vHalf = canvas halves; centre = this particle; radius = its radius — exactly how BlastRenderer fills GeoCtx per shape
            int px0, px1, py0, py1;
            if (anyGeo) { px0 = 0; py0 = 0; px1 = W - 1; py1 = H - 1; }   // a warp can pull any pixel into the disc, so scan the whole canvas (as RasterShape does)
            else
            {
                px0 = Mathf.Max(0, Mathf.FloorToInt(cx - radius));
                px1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + radius));
                py0 = Mathf.Max(0, Mathf.FloorToInt(cy - radius));
                py1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + radius));
            }
            int pHash = Hash(spec.seed, particleIndex, FldModifier, 7 + _layerSalt);   // a stable per-particle seed for PixelInfo.hash (modifiers combine it with x/y)

            // Per-particle 2D spin (T7): rotate this particle's OWN pixels in place. Each sample's LOCAL offset
            // (from the particle centre) is rotated by −spin, composed BEFORE the geometry InverseWarp fold — so a
            // warped/textured disc turns as a unit. The per-pixel order is: local offset → spin-rotate → geometry
            // fold → distance test. A plain disc is radially symmetric so spin shows nothing on it — which is why
            // spin lives ONLY on this modifier path (the fast path above can't display it, so it's left untouched
            // and byte-identical). Guarded on spin != 0f exactly: when 0 the rotation is skipped and the offsets fed
            // to the fold/test are the pre-T7 expressions verbatim, so a default asset stays byte-identical. Raster
            // bounds need NO change — rotation preserves the offset's length (a symmetric disc's extent is
            // unchanged) and an active geometry warp already forces the whole-canvas scan.
            float spin = Eval(layer.particleSpin, life, spec.seed, particleIndex, FldSpin);
            if (orientDeg != 0f) spin += orientDeg;   // swarm facing (S1) adds to the 2D spin fold; 0 ⇒ byte-identical
            bool doSpin = spin != 0f;
            float spinCos = 1f, spinSin = 0f;
            if (doSpin) { float sa = -spin * Mathf.Deg2Rad; spinCos = Mathf.Cos(sa); spinSin = Mathf.Sin(sa); }

            for (int y = py0; y <= py1; y++)
                for (int x = px0; x <= px1; x++)
                {
                    float sx = x + 0.5f, sy = y + 0.5f;
                    float wx = sx, wy = sy, dx, dy;
                    if (anyGeo)
                    {
                        // Sample offset lifted into canvas-centre space, spin-rotated about THIS particle's centre
                        // BEFORE the geometry fold. When spin is 0 the input is exactly the pre-T7 (sx-ccx, sy-ccy),
                        // so the folded result is bit-identical.
                        Vector2 geoIn;
                        if (doSpin)
                        {
                            float lox = sx - cx, loy = sy - cy;   // local offset from the particle centre
                            geoIn = new Vector2(c.x + (lox * spinCos - loy * spinSin),
                                                c.y + (lox * spinSin + loy * spinCos));
                        }
                        else geoIn = new Vector2(sx - ccx, sy - ccy);
                        Vector2 off = ApplyGeo(mods.geo, geoIn, phase, ctx);
                        wx = ccx + off.x; wy = ccy + off.y;   // geometry-warped absolute position (== sx,sy when no geo warp moves it)
                        dx = off.x - c.x; dy = off.y - c.y;   // warped position − this particle's centre (== sx-cx, sy-cy when no warp/spin)
                    }
                    else if (doSpin)
                    {
                        float lox = sx - cx, loy = sy - cy;   // spin the local offset in place (no geometry warp to fold)
                        dx = lox * spinCos - loy * spinSin;
                        dy = lox * spinSin + loy * spinCos;
                    }
                    else { dx = sx - cx; dy = sy - cy; }
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > radius) continue;
                    float edge = d <= inner ? 1f : 1f - Mathf.InverseLerp(inner, radius, d);
                    if (fillSpatial)   // spatial fill uses the (spun/warped) local offset (dx,dy)/radius, or canvas-anchored (Fixed); Solid/OverLife skip (byte-identical)
                    {
                        col = EvalFill(fill, life, dx / radius, dy / radius, x, y, W, H);
                        cr = col.r; cg = col.g; cb = col.b;
                        if (brightMul != 1f) { cr = Mathf.Clamp01(cr * brightMul); cg = Mathf.Clamp01(cg * brightMul); cb = Mathf.Clamp01(cb * brightMul); }
                    }
                    float baseA = alpha * col.a * edge;
                    if (baseA <= 0.002f) continue;
                    if (mods.AnyPix)
                    {
                        // RasterShape convention: the colour carries RGB with alpha 1, the real pixel alpha rides
                        // separately, and both are handed to the modifiers (which may recolour, fade, or drop).
                        Color pc = new Color(cr, cg, cb, 1f);
                        float pa = baseA;
                        float crossFrac = Mathf.Clamp01(d / Mathf.Max(0.001f, radius));   // 0 = centre, 1 = edge
                        if (!ApplyPix(mods.pix, ref pc, ref pa, x, y, wx, wy, frameIndex, crossFrac, life, pHash, W, H)) continue;
                        Over(buf, y * W + x, pc.r, pc.g, pc.b, pa);
                    }
                    else Over(buf, y * W + x, cr, cg, cb, baseA);
                }
        }

        // ── other stateless forms (G2): Crescent / Sparkle / Sprite ────────────────────
        // Resolve one candidate pixel's sample position, folding the particle's 2D spin (rotate the local
        // offset by −spin about the particle centre) and then the geometry modifiers, in the SAME order and
        // with the SAME arithmetic as DrawParticle's Disc modifier path — so a warped/spun/textured shape turns
        // as a unit. `dx/dy` = offset from the particle centre (the shape-local test coord); `wx/wy` = the
        // geometry-warped ABSOLUTE canvas position (for PixelInfo). When neither spin nor geo is active this is
        // exactly (sx−cx, sy−cy) and (sx, sy), so a plain shape is undisturbed.
        static void ResolveSample(float sx, float sy, float cx, float cy, float ccx, float ccy, Vector2 c,
                                  bool doSpin, float spinCos, float spinSin, bool anyGeo,
                                  GeometryModifier[] geo, float phase, in GeoCtx ctx,
                                  out float dx, out float dy, out float wx, out float wy)
        {
            wx = sx; wy = sy;
            if (anyGeo)
            {
                Vector2 geoIn;
                if (doSpin)
                {
                    float lox = sx - cx, loy = sy - cy;   // local offset from the particle centre
                    geoIn = new Vector2(c.x + (lox * spinCos - loy * spinSin),
                                        c.y + (lox * spinSin + loy * spinCos));
                }
                else geoIn = new Vector2(sx - ccx, sy - ccy);
                Vector2 off = ApplyGeo(geo, geoIn, phase, ctx);
                wx = ccx + off.x; wy = ccy + off.y;
                dx = off.x - c.x; dy = off.y - c.y;
            }
            else if (doSpin)
            {
                float lox = sx - cx, loy = sy - cy;
                dx = lox * spinCos - loy * spinSin;
                dy = lox * spinSin + loy * spinCos;
            }
            else { dx = sx - cx; dy = sy - cy; }
        }

        // Crescent: a disc with a second offset disc masked out. A pixel is lit when it's inside the main disc
        // (d ≤ radius) AND outside the bite disc (mdist > biteRadius). The bite disc's centre sits crescentOffset·
        // radius out in the crescentAngle direction; its radius is crescentBite·radius (both on the particle's own
        // life). edgeSoftness feathers BOTH rims: the outer edge exactly as Disc does (via `inner`), the bite edge
        // over a soft·radius band just OUTSIDE it — mirroring real Pyre's `(mnd−1)/innerSoft` bite feather
        // (normalised to the main radius), generalised to a bite disc of its own size.
        static void DrawCrescentBody(Color32[] buf, int W, int H, float cx, float cy, float radius, float alpha,
                                     Color col, float cr, float cg, float cb, float soft, float inner,
                                     PyrePlusSpec spec, PyrePlusLayer layer, int particleIndex, in ModSet mods, float phase,
                                     int frameIndex, float life, float brightMul, float orientDeg = 0f)
        {
            float bite = Mathf.Clamp01(Eval(layer.crescentBite, life, spec.seed, particleIndex, FldCrescentBite));
            float biteRadius = radius * bite;
            // Bite-disc centre in the shape-local (dx,dy) frame. The 2D centre pad (#12 part 2) wins when set; else
            // the legacy polar (crescentOffset, crescentAngle) — a null pair leaves this the ORIGINAL math verbatim,
            // so an un-migrated asset is byte-identical.
            float bx, by;
            if (layer.crescentCenterXAnim != null && layer.crescentCenterYAnim != null)
            {
                bx = Eval(layer.crescentCenterXAnim, life, spec.seed, particleIndex, FldCrescentCenterX) * radius;
                by = Eval(layer.crescentCenterYAnim, life, spec.seed, particleIndex, FldCrescentCenterY) * radius;
            }
            else
            {
                float ang = Eval(layer.crescentAngle, life, spec.seed, particleIndex, FldCrescentAngle) * Mathf.Deg2Rad;
                float off = Mathf.Clamp01(layer.crescentOffset) * radius;
                bx = off * Mathf.Cos(ang); by = off * Mathf.Sin(ang);
            }

            // Shape fill: hoisted col/cr/cg/cb (passed in) is the constant for the default OverLife / any Solid fill;
            // a spatial fill recomputes per lit pixel at (dx/radius, dy/radius) below. fillSpatial false ⇒ byte-identical.
            var fill = layer.shapeFill;
            bool fillSpatial = IsSpatialFill(fill);

            bool anyGeo = mods.AnyGeo;
            float ccx = W * 0.5f, ccy = H * 0.5f;
            Vector2 c = new Vector2(cx - ccx, cy - ccy);
            var ctx = new GeoCtx(ccx, ccy, c, radius);
            float spin = Eval(layer.particleSpin, life, spec.seed, particleIndex, FldSpin);
            if (orientDeg != 0f) spin += orientDeg;   // swarm facing (S1); 0 ⇒ byte-identical
            bool doSpin = spin != 0f;
            float spinCos = 1f, spinSin = 0f;
            if (doSpin) { float sa = -spin * Mathf.Deg2Rad; spinCos = Mathf.Cos(sa); spinSin = Mathf.Sin(sa); }
            int pHash = Hash(spec.seed, particleIndex, FldModifier, 7 + _layerSalt);

            int px0, px1, py0, py1;
            if (anyGeo) { px0 = 0; py0 = 0; px1 = W - 1; py1 = H - 1; }
            else
            {
                px0 = Mathf.Max(0, Mathf.FloorToInt(cx - radius));
                px1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + radius));
                py0 = Mathf.Max(0, Mathf.FloorToInt(cy - radius));
                py1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + radius));
            }

            for (int y = py0; y <= py1; y++)
                for (int x = px0; x <= px1; x++)
                {
                    float sx = x + 0.5f, sy = y + 0.5f;
                    ResolveSample(sx, sy, cx, cy, ccx, ccy, c, doSpin, spinCos, spinSin, anyGeo, mods.geo, phase, ctx,
                                  out float dx, out float dy, out float wx, out float wy);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > radius) continue;
                    float mdx = dx - bx, mdy = dy - by;
                    float mdist = Mathf.Sqrt(mdx * mdx + mdy * mdy);
                    float biteEdge;
                    if (biteRadius < 0.5f) biteEdge = 1f;                              // no meaningful bite → plain disc
                    else if (soft <= 0.001f) biteEdge = mdist >= biteRadius ? 1f : 0f; // hard bite edge
                    else biteEdge = Mathf.Clamp01((mdist - biteRadius) / (soft * radius));
                    if (biteEdge <= 0.001f) continue;
                    if (fillSpatial)   // spatial fill → this lit pixel's colour at its local (u,v) or canvas-anchored (Fixed); constant fills skip (byte-identical)
                    {
                        col = EvalFill(fill, life, dx / radius, dy / radius, x, y, W, H);
                        cr = col.r; cg = col.g; cb = col.b;
                        if (brightMul != 1f) { cr = Mathf.Clamp01(cr * brightMul); cg = Mathf.Clamp01(cg * brightMul); cb = Mathf.Clamp01(cb * brightMul); }
                    }
                    float outerEdge = d <= inner ? 1f : 1f - Mathf.InverseLerp(inner, radius, d);
                    float baseA = alpha * col.a * outerEdge * biteEdge;
                    if (baseA <= 0.002f) continue;
                    if (mods.AnyPix)
                    {
                        Color pc = new Color(cr, cg, cb, 1f);
                        float pa = baseA;
                        float crossFrac = Mathf.Clamp01(d / Mathf.Max(0.001f, radius));
                        if (!ApplyPix(mods.pix, ref pc, ref pa, x, y, wx, wy, frameIndex, crossFrac, life, pHash, W, H)) continue;
                        Over(buf, y * W + x, pc.r, pc.g, pc.b, pa);
                    }
                    else Over(buf, y * W + x, cr, cg, cb, baseA);
                }
        }

        // Star (V1): a filled star POLYGON — a 3N-gon of N arms with tips at radius R (= the size envelope, passed
        // in as `radius`) and TWO valley vertices per sector at radius r = R·(1−starLength). TWO valleys (not one)
        // so baseWidth thins the arms SYMMETRICALLY: a single valley placed `off` FROM the tip is inherently
        // asymmetric (close to tip k, far from tip k+1) and reads as a pinwheel skew — the defect this revision
        // fixes. Per sector between tip k (angle aTip) and tip k+1 (aTip+sector): valleyA sits offA past tip k,
        // valleyB sits offB before tip k+1 (i.e. at aTip+sector−offB), BOTH at r, joined by a flat base chord.
        //   offA = clamp(halfSector·baseWidth + skew, 0.02·sector, 0.98·sector)
        //   offB = clamp(halfSector·baseWidth − skew, 0.02·sector, 0.98·sector)
        // so skew shifts BOTH valleys the SAME signed way (a true pinwheel — the arm rotates but keeps its width),
        // while baseWidth narrows both toward their tips symmetrically. With these symmetric clamps offA+offB ≤
        // sector ALWAYS (unclamped sum = sector·baseWidth ≤ sector; any clamp that raises one offset to 0.98·sector
        // drives the other to 0.02·sector, capping the sum at sector), so the two valleys never CROSS — at worst
        // they COINCIDE (offA+offB = sector), which is exactly the bw=1 default and the extreme-skew case; that
        // collapses to a single midpoint valley (the classical two-segment star). DEFAULT BYTE-IDENTITY: at bw=1,
        // skew=0, offA=offB=halfSector (unclamped) so vA=offA=halfSector and vB=sector−offB=sector−halfSector, and
        // since sector==halfSector+halfSector exactly (fl(2a)==2·fl(a) for round-to-nearest) the exact-difference
        // 2·halfSector−halfSector gives vB==halfSector==vA → coincide → the single-valley branch runs the SAME two
        // segments (tip→valley at halfSector, valley→nextTip) with the SAME endpoints the pre-fix star used, so the
        // classical star renders bit-for-bit unchanged; the flat base chord has zero length there and is unreachable.
        //
        // A star polygon is star-shaped about its centre, so the inside test is RADIAL: for each pixel's
        // (spun/warped) local offset take θ = atan2(dy,dx), find the ONE boundary edge (tip→valleyA, the base chord
        // valleyA→valleyB, or valleyB→next tip) whose angular slot contains θ, and intersect the ray with it. The
        // boundary distance `bound` along the ray is the ray-segment cross-product solve  t = cross(E,P1)/cross(E,D)
        // (E = P2−P1, D = the ray unit vector, cross(A,B) = Ax·By − Ay·Bx) — derived so that, θ already being between
        // the two endpoint angles, t is the positive crossing distance. Lit iff d ≤ bound; edgeSoftness feathers the
        // rim radially per ray (inner = bound·(1−soft)), the Disc rim idiom along the ray. Tips are at R (valleys at
        // r < R), so the OUTER bound is R everywhere (the d > R early-out is exact). Spin + geometry warps fold
        // through ResolveSample in the SAME order as the Disc modifier path; the shape fill maps (u,v) = (dx/R, dy/R).
        static void DrawStarBody(Color32[] buf, int W, int H, float cx, float cy, float radius, float alpha,
                                 Color col, float cr, float cg, float cb, float soft,
                                 PyrePlusSpec spec, PyrePlusLayer layer, int particleIndex, in ModSet mods, float phase,
                                 int frameIndex, float life, float brightMul, float orientDeg = 0f)
        {
            float R = radius;
            int N = Mathf.Clamp(layer.starArms, 2, 20);
            float len = Mathf.Clamp01(Eval(layer.starLength, life, spec.seed, particleIndex, FldStarLen));
            float rIn = Mathf.Max(0.5f, R * (1f - len));                              // valley radius, floored so it never collapses to a point
            float baseW = Mathf.Clamp(Eval(layer.starBaseWidth, life, spec.seed, particleIndex, FldStarBase), 0.1f, 1f);
            float skew = Eval(layer.starSkew, life, spec.seed, particleIndex, FldStarSkew) * Mathf.Deg2Rad;

            float sector = 2f * Mathf.PI / N;        // angular span between adjacent tips
            float halfSector = Mathf.PI / N;         // half of it — the classical (single-valley) midpoint
            // TWO valleys per sector (see the header): valleyA offA past tip k, valleyB offB before tip k+1. skew
            // shifts both the same signed way (pinwheel), baseW thins both toward their tips. Clamp both away from
            // the tips (a degenerate zero-length edge / divide-by-zero) — symmetric so offA+offB ≤ sector always.
            float lo = 0.02f * sector, hi = 0.98f * sector;
            float offA = Mathf.Clamp(halfSector * baseW + skew, lo, hi);   // valleyA angle past tip k
            float offB = Mathf.Clamp(halfSector * baseW - skew, lo, hi);   // valleyB angle before tip k+1
            float vA = offA;                         // valleyA relative angle from the tip
            float vB = sector - offB;                // valleyB relative angle from the tip
            // Coincide/cross guard: offA+offB ≤ sector ⇒ vA ≤ vB ALWAYS, so this is true only when they meet exactly
            // (the bw=1 default, or an extreme skew clamped to both rails). Then the star has a single midpoint
            // valley — the classical two-segment shape — and at the default vA == halfSector, keeping it byte-identical.
            bool coincide = vB <= vA;
            const float TIP = Mathf.PI * 0.5f;       // tip 0 points UP (+90°), matching the polygon convention

            var fill = layer.shapeFill;
            bool fillSpatial = IsSpatialFill(fill);

            bool anyGeo = mods.AnyGeo;
            float ccx = W * 0.5f, ccy = H * 0.5f;
            Vector2 c = new Vector2(cx - ccx, cy - ccy);
            var ctx = new GeoCtx(ccx, ccy, c, radius);
            float spin = Eval(layer.particleSpin, life, spec.seed, particleIndex, FldSpin);
            if (orientDeg != 0f) spin += orientDeg;   // swarm facing (S1) folds into the 2D spin, exactly like Disc/Crescent
            bool doSpin = spin != 0f;
            float spinCos = 1f, spinSin = 0f;
            if (doSpin) { float sa = -spin * Mathf.Deg2Rad; spinCos = Mathf.Cos(sa); spinSin = Mathf.Sin(sa); }
            int pHash = Hash(spec.seed, particleIndex, FldModifier, 7 + _layerSalt);

            int px0, px1, py0, py1;
            if (anyGeo) { px0 = 0; py0 = 0; px1 = W - 1; py1 = H - 1; }   // a warp can pull any pixel in → whole-canvas scan
            else
            {
                px0 = Mathf.Max(0, Mathf.FloorToInt(cx - radius));       // tips reach exactly R, so an R box bounds the star
                px1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + radius));
                py0 = Mathf.Max(0, Mathf.FloorToInt(cy - radius));
                py1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + radius));
            }

            for (int y = py0; y <= py1; y++)
                for (int x = px0; x <= px1; x++)
                {
                    float sx = x + 0.5f, sy = y + 0.5f;
                    ResolveSample(sx, sy, cx, cy, ccx, ccy, c, doSpin, spinCos, spinSin, anyGeo, mods.geo, phase, ctx,
                                  out float dx, out float dy, out float wx, out float wy);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > R) continue;                                 // beyond the tips — nothing is inside

                    // Locate the angular slot: θ's arm sector, then which of that sector's THREE edges spans it.
                    float th = Mathf.Atan2(dy, dx);
                    float phi = th - TIP;
                    float ph = phi - Mathf.Floor(phi / sector) * sector;  // [0, sector): angle past this arm's tip
                    float aTip = th - ph;                                 // absolute angle of the tip starting this slot (= TIP + k·sector)
                    float a1, r1, a2, r2;
                    if (coincide)
                    {
                        // Single midpoint valley (bw=1 default / extreme skew) → the classical two-segment star.
                        // vA == halfSector at the default, so this is byte-identical to the pre-fix star.
                        if (ph < vA) { a1 = aTip;      r1 = R;   a2 = aTip + vA;     r2 = rIn; }   // tip → valley
                        else         { a1 = aTip + vA; r1 = rIn; a2 = aTip + sector; r2 = R;   }   // valley → next tip
                    }
                    else if (ph < vA) { a1 = aTip;      r1 = R;   a2 = aTip + vA;     r2 = rIn; }  // tip → valleyA
                    else if (ph < vB) { a1 = aTip + vA; r1 = rIn; a2 = aTip + vB;     r2 = rIn; }  // valleyA → valleyB (flat base chord)
                    else              { a1 = aTip + vB; r1 = rIn; a2 = aTip + sector; r2 = R;   }  // valleyB → next tip

                    // Ray-segment intersection distance along the ray at angle θ (see the method header).
                    float Dx = Mathf.Cos(th), Dy = Mathf.Sin(th);
                    float p1x = r1 * Mathf.Cos(a1), p1y = r1 * Mathf.Sin(a1);
                    float p2x = r2 * Mathf.Cos(a2), p2y = r2 * Mathf.Sin(a2);
                    float ex = p2x - p1x, ey = p2y - p1y;
                    float denom = ex * Dy - ey * Dx;                      // cross(E, D)
                    float bound = Mathf.Abs(denom) < 1e-6f ? R : (ex * p1y - ey * p1x) / denom;   // cross(E, P1) / cross(E, D)
                    if (bound <= 0f) continue;                           // safety: degenerate ray
                    if (d > bound) continue;                             // outside the star along this ray (a valley notch)

                    float rInner = bound * (1f - soft);                  // Disc rim idiom, radial: full alpha inside, feather to the boundary
                    float edge = d <= rInner ? 1f : 1f - Mathf.InverseLerp(rInner, bound, d);
                    if (fillSpatial)   // spatial fill → this pixel's colour at its (spun/warped) local (u,v) or canvas-anchored (Fixed); constant fills skip
                    {
                        col = EvalFill(fill, life, dx / R, dy / R, x, y, W, H);
                        cr = col.r; cg = col.g; cb = col.b;
                        if (brightMul != 1f) { cr = Mathf.Clamp01(cr * brightMul); cg = Mathf.Clamp01(cg * brightMul); cb = Mathf.Clamp01(cb * brightMul); }
                    }
                    float baseA = alpha * col.a * edge;
                    if (baseA <= 0.002f) continue;
                    if (mods.AnyPix)
                    {
                        Color pc = new Color(cr, cg, cb, 1f);
                        float pa = baseA;
                        float crossFrac = Mathf.Clamp01(d / Mathf.Max(0.001f, R));
                        if (!ApplyPix(mods.pix, ref pc, ref pa, x, y, wx, wy, frameIndex, crossFrac, life, pHash, W, H)) continue;
                        Over(buf, y * W + x, pc.r, pc.g, pc.b, pa);
                    }
                    else Over(buf, y * W + x, cr, cg, cb, baseA);
                }
        }

        // Polygon: a flat filled regular convex N-gon (polygonSides), the 2D counterpart to the 3D Box/Pyramid. Every
        // vertex sits at the circumradius R (= `size`·sizeMul), so an R box bounds it exactly like the Star. It REUSES
        // the Star's per-ray boundary idiom UNCHANGED — resolve each pixel's sample (spin + geometry warps folded in by
        // ResolveSample, identically to Disc/Star), take θ = atan2, find the ONE tip→tip edge spanning θ's angular
        // sector (no valleys, so a single edge per sector, both endpoints at R), and intersect the ray with it to get
        // the boundary distance `bound`; a pixel is inside iff its distance d ≤ bound. edgeSoftness feathers the rim
        // radially (the Disc rim idiom), shapeFill (constant or spatial) colours it, brightMul shades RGB, and the pix
        // modifiers apply exactly as they do for the Star. A base rotation (half a sector for an even side count, 0 for
        // odd) rests an even N-gon on a flat edge (a square sits flat, not a diamond) while keeping an odd N pointing a
        // vertex up. Byte-identity: this is a NEW branch reached ONLY by the new Polygon form — no existing spec's
        // Disc/Star/etc. path is touched.
        static void DrawPolygonBody(Color32[] buf, int W, int H, float cx, float cy, float radius, float alpha,
                                    Color col, float cr, float cg, float cb, float soft,
                                    PyrePlusSpec spec, PyrePlusLayer layer, int particleIndex, in ModSet mods, float phase,
                                    int frameIndex, float life, float brightMul, float orientDeg = 0f)
        {
            float R = radius;
            int N = Mathf.Clamp(layer.polygonSides, 3, 12);
            float sector = 2f * Mathf.PI / N;        // angular span between adjacent vertices
            // Base orientation: an even N is turned half a sector so a flat EDGE (not a vertex) faces up/down — a
            // square sits flat, a hexagon flat-top; an odd N keeps a vertex up, which already yields a flat bottom
            // edge (an upright triangle / pentagon). This is a fixed geometric offset, independent of particleSpin.
            float baseRot = (N % 2 == 0) ? sector * 0.5f : 0f;
            const float UP = Mathf.PI * 0.5f;        // vertex 0 points UP (+90°), matching the Star's tip convention
            float tip0 = UP + baseRot;               // absolute angle of vertex 0

            var fill = layer.shapeFill;
            bool fillSpatial = IsSpatialFill(fill);

            bool anyGeo = mods.AnyGeo;
            float ccx = W * 0.5f, ccy = H * 0.5f;
            Vector2 c = new Vector2(cx - ccx, cy - ccy);
            var ctx = new GeoCtx(ccx, ccy, c, radius);
            float spin = Eval(layer.particleSpin, life, spec.seed, particleIndex, FldSpin);
            if (orientDeg != 0f) spin += orientDeg;   // swarm facing (S1) folds into the 2D spin, exactly like Disc/Star
            bool doSpin = spin != 0f;
            float spinCos = 1f, spinSin = 0f;
            if (doSpin) { float sa = -spin * Mathf.Deg2Rad; spinCos = Mathf.Cos(sa); spinSin = Mathf.Sin(sa); }
            int pHash = Hash(spec.seed, particleIndex, FldModifier, 7 + _layerSalt);

            int px0, px1, py0, py1;
            if (anyGeo) { px0 = 0; py0 = 0; px1 = W - 1; py1 = H - 1; }   // a warp can pull any pixel in → whole-canvas scan
            else
            {
                px0 = Mathf.Max(0, Mathf.FloorToInt(cx - radius));       // vertices reach exactly R, so an R box bounds it
                px1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + radius));
                py0 = Mathf.Max(0, Mathf.FloorToInt(cy - radius));
                py1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + radius));
            }

            for (int y = py0; y <= py1; y++)
                for (int x = px0; x <= px1; x++)
                {
                    float sx = x + 0.5f, sy = y + 0.5f;
                    ResolveSample(sx, sy, cx, cy, ccx, ccy, c, doSpin, spinCos, spinSin, anyGeo, mods.geo, phase, ctx,
                                  out float dx, out float dy, out float wx, out float wy);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > R) continue;                                 // beyond the vertices — nothing is inside

                    // The one edge spanning θ's sector: vertex k → vertex k+1, both at radius R.
                    float th = Mathf.Atan2(dy, dx);
                    float phi = th - tip0;
                    float ph = phi - Mathf.Floor(phi / sector) * sector;  // [0, sector): angle past this sector's first vertex
                    float aVert = th - ph;                                // absolute angle of that vertex
                    float a1 = aVert, a2 = aVert + sector;                // the sector's two vertices

                    // Ray-segment intersection distance along the ray at angle θ (both endpoints at radius R).
                    float Dx = Mathf.Cos(th), Dy = Mathf.Sin(th);
                    float p1x = R * Mathf.Cos(a1), p1y = R * Mathf.Sin(a1);
                    float p2x = R * Mathf.Cos(a2), p2y = R * Mathf.Sin(a2);
                    float ex = p2x - p1x, ey = p2y - p1y;
                    float denom = ex * Dy - ey * Dx;                      // cross(E, D)
                    float bound = Mathf.Abs(denom) < 1e-6f ? R : (ex * p1y - ey * p1x) / denom;   // cross(E, P1) / cross(E, D)
                    if (bound <= 0f) continue;                           // safety: degenerate ray
                    if (d > bound) continue;                             // outside the polygon along this ray

                    float rInner = bound * (1f - soft);                  // Disc rim idiom, radial: full alpha inside, feather to the boundary
                    float edge = d <= rInner ? 1f : 1f - Mathf.InverseLerp(rInner, bound, d);
                    if (fillSpatial)   // spatial fill → this pixel's colour at its (spun/warped) local (u,v) or canvas-anchored (Fixed); constant fills skip
                    {
                        col = EvalFill(fill, life, dx / R, dy / R, x, y, W, H);
                        cr = col.r; cg = col.g; cb = col.b;
                        if (brightMul != 1f) { cr = Mathf.Clamp01(cr * brightMul); cg = Mathf.Clamp01(cg * brightMul); cb = Mathf.Clamp01(cb * brightMul); }
                    }
                    float baseA = alpha * col.a * edge;
                    if (baseA <= 0.002f) continue;
                    if (mods.AnyPix)
                    {
                        Color pc = new Color(cr, cg, cb, 1f);
                        float pa = baseA;
                        float crossFrac = Mathf.Clamp01(d / Mathf.Max(0.001f, R));
                        if (!ApplyPix(mods.pix, ref pc, ref pa, x, y, wx, wy, frameIndex, crossFrac, life, pHash, W, H)) continue;
                        Over(buf, y * W + x, pc.r, pc.g, pc.b, pa);
                    }
                    else Over(buf, y * W + x, cr, cg, cb, baseA);
                }
        }

        // Sparkle: overlay a virtual grid of `sparkleSize`-px cells on the disc. A cell is LIT this frame iff its
        // stable presence draw < density AND its per-frame twinkle draw passes — both from the Hash funnel, no
        // state, so any frame renders standalone. Presence keys (seed, particleIndex, FldSparkle, cellIndex) so
        // the same cells are the candidates every frame (stable identity); twinkle keys the frame bucket in too
        // (cellIndex ^ frameSalt) so each candidate flickers on/off — the deterministic mirror of Pyre's
        // stable-but-twinkling sparkles. frameSalt is the particle's OWN-life frame bucket, so a swarm particle's
        // twinkle tracks its own clock. Lit cells are hard full-colour/alpha pixels — no edge falloff; pixels
        // past the disc radius are never lit.
        static void DrawSparkleBody(Color32[] buf, int W, int H, float cx, float cy, float radius, float alpha,
                                    Color col, float cr, float cg, float cb,
                                    PyrePlusSpec spec, PyrePlusLayer layer, int particleIndex, in ModSet mods, float phase,
                                    int frameIndex, float life, float brightMul, float orientDeg = 0f)
        {
            float density = Mathf.Clamp01(Eval(layer.sparkleDensity, life, spec.seed, particleIndex, FldSparkle));
            if (density <= 0.001f) return;
            float baseAlpha = alpha * col.a;
            // Shape fill: the hoisted col/baseAlpha are the constant for the default OverLife / any Solid fill; a
            // spatial fill recomputes per lit cell below. The hoisted early-out is only valid for a constant fill
            // (a spatial fill's centre alpha may be ~0 while lit cells elsewhere aren't), so it's gated on !spatial.
            var fill = layer.shapeFill;
            bool fillSpatial = IsSpatialFill(fill);
            if (!fillSpatial && baseAlpha <= 0.002f) return;
            int cell = Mathf.Clamp(layer.sparkleSize, 1, 4);
            int frames = Mathf.Max(1, spec.frameCount);
            // Own-life frame bucket → the twinkle clock. The +1 keeps it nonzero so a cell's twinkle key
            // (cellIndex ^ frameSalt) can never coincide with its presence key (cellIndex, frameSalt 0 would).
            int frameSalt = 1 + Mathf.FloorToInt(Mathf.Clamp01(life) * frames);

            bool anyGeo = mods.AnyGeo;
            float ccx = W * 0.5f, ccy = H * 0.5f;
            Vector2 c = new Vector2(cx - ccx, cy - ccy);
            var ctx = new GeoCtx(ccx, ccy, c, radius);
            float spin = Eval(layer.particleSpin, life, spec.seed, particleIndex, FldSpin);
            if (orientDeg != 0f) spin += orientDeg;   // swarm facing (S1); 0 ⇒ byte-identical
            bool doSpin = spin != 0f;
            float spinCos = 1f, spinSin = 0f;
            if (doSpin) { float sa = -spin * Mathf.Deg2Rad; spinCos = Mathf.Cos(sa); spinSin = Mathf.Sin(sa); }
            int pHash = Hash(spec.seed, particleIndex, FldModifier, 7 + _layerSalt);

            int px0, px1, py0, py1;
            if (anyGeo) { px0 = 0; py0 = 0; px1 = W - 1; py1 = H - 1; }
            else
            {
                px0 = Mathf.Max(0, Mathf.FloorToInt(cx - radius));
                px1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + radius));
                py0 = Mathf.Max(0, Mathf.FloorToInt(cy - radius));
                py1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + radius));
            }

            for (int y = py0; y <= py1; y++)
                for (int x = px0; x <= px1; x++)
                {
                    float sx = x + 0.5f, sy = y + 0.5f;
                    ResolveSample(sx, sy, cx, cy, ccx, ccy, c, doSpin, spinCos, spinSin, anyGeo, mods.geo, phase, ctx,
                                  out float dx, out float dy, out float wx, out float wy);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > radius) continue;
                    int gx = Mathf.FloorToInt(dx / cell), gy = Mathf.FloorToInt(dy / cell);
                    // Spatial-hash the cell to one int; XOR the golden-ratio constant so the origin cell (0,0)
                    // maps to a nonzero id — otherwise its presence draw would key (…, FldSparkle, 0), colliding
                    // with a MinMax sparkleDensity's own d=0 draw in this same field stream.
                    int cellIndex = ((gx * 73856093) ^ (gy * 19349663)) ^ unchecked((int)0x9E3779B9);
                    float presence = Hash01(spec.seed, particleIndex, FldSparkle, cellIndex + _layerSalt);
                    if (presence >= density) continue;                                  // not a candidate cell
                    float twinkle = Hash01(spec.seed, particleIndex, FldSparkle, (cellIndex ^ frameSalt) + _layerSalt);
                    if (twinkle >= 0.5f) continue;                                       // candidate, but off this frame
                    float sr = cr, sg = cg, sb = cb, sa = baseAlpha;
                    if (fillSpatial)   // spatial fill → this cell's colour + alpha at its local (u,v) or canvas-anchored (Fixed); constant fills skip (byte-identical)
                    {
                        col = EvalFill(fill, life, dx / radius, dy / radius, x, y, W, H);
                        sr = col.r; sg = col.g; sb = col.b; sa = alpha * col.a;
                        if (brightMul != 1f) { sr = Mathf.Clamp01(sr * brightMul); sg = Mathf.Clamp01(sg * brightMul); sb = Mathf.Clamp01(sb * brightMul); }
                    }
                    if (mods.AnyPix)
                    {
                        Color pc = new Color(sr, sg, sb, 1f);
                        float pa = sa;
                        float crossFrac = Mathf.Clamp01(d / Mathf.Max(0.001f, radius));
                        if (!ApplyPix(mods.pix, ref pc, ref pa, x, y, wx, wy, frameIndex, crossFrac, life, pHash, W, H)) continue;
                        Over(buf, y * W + x, pc.r, pc.g, pc.b, pa);
                    }
                    else Over(buf, y * W + x, sr, sg, sb, sa);
                }
        }

        // Sprite: stamp layer.spriteImage's pixels, scaled so its larger dimension maps to 2·radius, centred on
        // the particle, rotated by the shared spin and folded through the geometry warps (via ResolveSample —
        // spin-then-warp, the Disc order), point-sampled (nearest) from the sprite's rect within its texture.
        // Tinted by the gradient when spriteTint, then alpha envelope × brightMul, PixelModifiers, Over. Returns
        // false (→ Disc fallback) when the sprite/texture is null or not Read/Write-enabled (GetPixels32 throws,
        // caught once in TryGetTexturePixels). True once it has taken responsibility for the stamp.
        static bool DrawSpriteBody(Color32[] buf, int W, int H, float cx, float cy, float radius, float alpha,
                                   Color col, float brightMul, PyrePlusSpec spec, PyrePlusLayer layer, int particleIndex,
                                   in ModSet mods, float phase, int frameIndex, float life, float orientDeg = 0f)
        {
            var sprite = layer.spriteImage;
            if (sprite == null || sprite.texture == null) return false;
            if (!TryGetTexturePixels(sprite.texture, out var px)) return false;   // non-readable texture → fallback

            var tex = sprite.texture;
            int texW = tex.width;
            Rect tr = sprite.textureRect;
            int rx = Mathf.FloorToInt(tr.x), ry = Mathf.FloorToInt(tr.y);
            int rw = Mathf.Max(1, Mathf.FloorToInt(tr.width)), rh = Mathf.Max(1, Mathf.FloorToInt(tr.height));
            float scale = (2f * radius) / Mathf.Max(rw, rh);
            if (scale <= 1e-4f) return true;   // radius collapsed — nothing to stamp, but the sprite HAS handled it

            bool anyGeo = mods.AnyGeo;
            float ccx = W * 0.5f, ccy = H * 0.5f;
            Vector2 c = new Vector2(cx - ccx, cy - ccy);
            var ctx = new GeoCtx(ccx, ccy, c, radius);
            float spin = Eval(layer.particleSpin, life, spec.seed, particleIndex, FldSpin);
            if (orientDeg != 0f) spin += orientDeg;   // swarm facing (S1); 0 ⇒ byte-identical
            bool doSpin = spin != 0f;
            float spinCos = 1f, spinSin = 0f;
            if (doSpin) { float sa = -spin * Mathf.Deg2Rad; spinCos = Mathf.Cos(sa); spinSin = Mathf.Sin(sa); }
            int pHash = Hash(spec.seed, particleIndex, FldModifier, 7 + _layerSalt);

            float box = radius * 1.5f;   // generous bbox to cover the rotated stamp (matches Pyre's RasterSprite)
            int px0, px1, py0, py1;
            if (anyGeo) { px0 = 0; py0 = 0; px1 = W - 1; py1 = H - 1; }
            else
            {
                px0 = Mathf.Max(0, Mathf.FloorToInt(cx - box));
                px1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + box));
                py0 = Mathf.Max(0, Mathf.FloorToInt(cy - box));
                py1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + box));
            }

            bool tint = layer.spriteTint;
            float tr2 = tint ? col.r : 1f, tg2 = tint ? col.g : 1f, tb2 = tint ? col.b : 1f, ta2 = tint ? col.a : 1f;
            for (int y = py0; y <= py1; y++)
                for (int x = px0; x <= px1; x++)
                {
                    float sx = x + 0.5f, sy = y + 0.5f;
                    ResolveSample(sx, sy, cx, cy, ccx, ccy, c, doSpin, spinCos, spinSin, anyGeo, mods.geo, phase, ctx,
                                  out float dx, out float dy, out float wx, out float wy);
                    int su = Mathf.FloorToInt(dx / scale + rw * 0.5f);
                    int sv = Mathf.FloorToInt(dy / scale + rh * 0.5f);
                    if (su < 0 || su >= rw || sv < 0 || sv >= rh) continue;
                    Color32 sc = px[(ry + sv) * texW + (rx + su)];   // GetPixels32 is row-major, bottom-left origin
                    if (sc.a == 0) continue;
                    float outR = (sc.r / 255f) * tr2, outG = (sc.g / 255f) * tg2, outB = (sc.b / 255f) * tb2;
                    if (brightMul != 1f)
                    {
                        outR = Mathf.Clamp01(outR * brightMul);
                        outG = Mathf.Clamp01(outG * brightMul);
                        outB = Mathf.Clamp01(outB * brightMul);
                    }
                    float outA = (sc.a / 255f) * alpha * ta2;
                    if (outA <= 0.002f) continue;
                    if (mods.AnyPix)
                    {
                        Color pc = new Color(outR, outG, outB, 1f);
                        float pa = outA;
                        float crossFrac = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / Mathf.Max(0.001f, radius));
                        if (!ApplyPix(mods.pix, ref pc, ref pa, x, y, wx, wy, frameIndex, crossFrac, life, pHash, W, H)) continue;
                        Over(buf, y * W + x, pc.r, pc.g, pc.b, pa);
                    }
                    else Over(buf, y * W + x, outR, outG, outB, outA);
                }
            return true;
        }

        // Full-texture pixel cache for the Sprite form, keyed by Texture2D. GetPixels32 needs the texture
        // Read/Write-enabled; a non-readable texture throws → caught, the entry is cached as null so we don't
        // retry every frame, and callers treat null as "not sampleable" (→ Disc fallback). Purity holds: the
        // texture's CONTENT is itself an input, so memoising the read preserves same-inputs-same-output; a
        // texture edited/reimported at author time would read stale until a domain reload clears the cache
        // (mirrors real Pyre's own sprite cache, which exposes an explicit clear for exactly that).
        static readonly System.Collections.Generic.Dictionary<Texture2D, Color32[]> _spritePixelCache =
            new System.Collections.Generic.Dictionary<Texture2D, Color32[]>();

        static bool TryGetTexturePixels(Texture2D tex, out Color32[] px)
        {
            px = null;
            if (tex == null) return false;
            if (_spritePixelCache.TryGetValue(tex, out px)) return px != null;
            Color32[] got;
            try { got = tex.GetPixels32(); }
            catch { got = null; }
            _spritePixelCache[tex] = got;
            px = got;
            return got != null;
        }

        // A deterministic [0,1) draw straight from the FNV Hash — same funnel as everything else, no per-pixel
        // System.Random allocation (used for the Sparkle presence/twinkle draws, evaluated per candidate pixel).
        static float Hash01(int a, int b, int c, int d)
        {
            uint h = (uint)Hash(a, b, c, d);
            return (h & 0x00FFFFFFu) / 16777216f;   // top of the 24-bit mantissa → [0,1)
        }

        // ── Streak (S1): an anchor-biased comet-tail capsule (Issue 1) ───────────────
        // The particle position (cx,cy) is an ANCHOR POINT on the streak set by streakAnchor: the streak reaches
        // `behind` (= anchor·len) toward the tail and `ahead` (= (1−anchor)·len) toward the tip, with behind +
        // ahead == len. Anchor 0.5 (the default) centres it, so growing Length extends the streak symmetrically
        // rather than sliding it off the particle. Per pixel we resolve the sample (folding any geometry warp,
        // exactly like the Disc path), take (f,s) = (forward, side) coords in the rotated streak frame, and light
        // it when −behind ≤ f ≤ ahead and |s| ≤ w/2. Sides feather over the outer `edgeSoftness` fraction of the
        // half-width; BOTH ends feather over the `streakSoftTip` fraction of their own length. The shape fill maps
        // (u,v) = (s/(w/2), 2·(f+behind)/len − 1) — so a SPATIAL fill sweeps side-to-side in u and tail(−1)→tip(+1) in v.
        //   Forward: default up/+y; when the swarm ORIENTS this particle (oriented == swarm on && swarmOrient !=
        //   None), forward math-angle = orientDeg (the target direction) + own spin; otherwise 90° + spin (up).
        //   orientDeg alone can't tell "no orient" from "Outward pointing +x" (both 0°), so the explicit `oriented`
        //   flag guards the up-default — unlike every other form, whose default rotation is genuinely 0.
        // lenByIndex (slice 3, default 1) multiplies the LENGTH only — the Bars barTaper path where an index taper
        // graduates length while width stays uniform. len·1 is bit-exact, so the default keeps the streak byte-
        // identical; width always rides the plain sizeMul, never lenByIndex.
        static void DrawStreakBody(Color32[] buf, int W, int H, float cx, float cy, float own,
                                   PyrePlusSpec spec, PyrePlusLayer layer, int particleIndex, in ModSet mods, float phase,
                                   int frameIndex, float sizeMul, float brightMul, float orientDeg, float lenByIndex = 1f)
        {
            float len = Mathf.Max(0f, Eval(layer.streakLength, own, spec.seed, particleIndex, FldStreakLen)) * sizeMul * lenByIndex;
            if (len < 1f) return;
            float w = Mathf.Max(0f, Eval(layer.streakWidth, own, spec.seed, particleIndex, FldStreakWidth)) * sizeMul;
            if (w < 0.5f) return;
            float alpha = Mathf.Clamp01(Eval(layer.alpha, own, spec.seed, particleIndex, FldAlpha));
            if (alpha <= 0.002f) return;

            // Shared per-particle travel (T7) — moves the streak's ROOT, same guard/keys as every other form.
            if (!(IsStaticZero(layer.particlePathX) && IsStaticZero(layer.particlePathY)))
            {
                cx += Eval(layer.particlePathX, own, spec.seed, particleIndex, FldPathX);
                cy += Eval(layer.particlePathY, own, spec.seed, particleIndex, FldPathY);
            }

            float spin = Eval(layer.particleSpin, own, spec.seed, particleIndex, FldSpin);
            bool oriented = layer.swarmEnabled && layer.swarmOrient != SwarmOrient.None;
            float fwdDeg = (oriented ? orientDeg : 90f) + spin;
            float fr = fwdDeg * Mathf.Deg2Rad;
            float Fx = Mathf.Cos(fr), Fy = Mathf.Sin(fr);   // forward unit vector (screen, y-up)
            float Sx = -Fy, Sy = Fx;                        // side = forward rotated +90° CCW

            float halfW = w * 0.5f;
            // Anchor bias (Issue 1): the particle sits at fraction `bias` from the TAIL, so the streak reaches
            // `behind` toward the tail (f < 0) and `ahead` toward the tip (f > 0), with behind + ahead == len.
            // bias 0.5 (the default) splits Length symmetrically, so growing Length extends the streak both ways
            // instead of sliding it off the particle — the particle is a STABLE anchor point on the streak.
            float bias = Mathf.Clamp01(layer.streakAnchor);
            float behind = bias * len;
            float ahead = (1f - bias) * len;
            float soft = Mathf.Clamp01(layer.edgeSoftnessAnim != null
                ? Eval(layer.edgeSoftnessAnim, own, spec.seed, particleIndex, FldEdgeSoftness) : layer.edgeSoftness);
            float sideInner = halfW * (1f - soft);          // |s| ≤ sideInner is full alpha; feathers to 0 at halfW
            float softTip = Mathf.Clamp01(layer.streakSoftTip);
            // Feather BOTH ends by softTip (symmetric, so a centred streak reads as a light streak fading at each
            // tip — a hard cut on one end would look lopsided now that the default anchor is centred). Each end
            // feathers over softTip·(that end's own length): f full to aheadStart then → 0 at ahead; and the
            // mirror at the behind end.
            float aheadStart = ahead * (1f - softTip);
            float behindStart = behind * (1f - softTip);

            var fill = layer.shapeFill;
            bool fillSpatial = IsSpatialFill(fill);
            Color col = fill != null ? fill.Evaluate(own, 0f, 0f) : Color.white;
            float cr = col.r, cg = col.g, cb = col.b;
            if (brightMul != 1f) { cr = Mathf.Clamp01(cr * brightMul); cg = Mathf.Clamp01(cg * brightMul); cb = Mathf.Clamp01(cb * brightMul); }

            bool anyGeo = mods.AnyGeo;
            float ccx = W * 0.5f, ccy = H * 0.5f;
            Vector2 c = new Vector2(cx - ccx, cy - ccy);
            float extent = Mathf.Max(ahead, behind) + halfW + 2f;
            var ctx = new GeoCtx(ccx, ccy, c, extent);
            int pHash = Hash(spec.seed, particleIndex, FldModifier, 7 + _layerSalt);

            int px0, px1, py0, py1;
            if (anyGeo) { px0 = 0; py0 = 0; px1 = W - 1; py1 = H - 1; }
            else
            {
                px0 = Mathf.Max(0, Mathf.FloorToInt(cx - extent));
                px1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + extent));
                py0 = Mathf.Max(0, Mathf.FloorToInt(cy - extent));
                py1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + extent));
            }

            for (int y = py0; y <= py1; y++)
                for (int x = px0; x <= px1; x++)
                {
                    float sx = x + 0.5f, sy = y + 0.5f;
                    // No spin fold in ResolveSample — the streak's rotation lives in (Fx,Fy); ResolveSample only
                    // folds the geometry warps (spin off: doSpin=false, cos=1, sin=0).
                    ResolveSample(sx, sy, cx, cy, ccx, ccy, c, false, 1f, 0f, anyGeo, mods.geo, phase, ctx,
                                  out float dx, out float dy, out float wx, out float wy);
                    float f = dx * Fx + dy * Fy;
                    if (f < -behind || f > ahead) continue;
                    float s = dx * Sx + dy * Sy;
                    float as_ = Mathf.Abs(s);
                    if (as_ > halfW) continue;
                    float sideEdge = as_ <= sideInner ? 1f : 1f - Mathf.InverseLerp(sideInner, halfW, as_);
                    // End feather toward whichever end this pixel sits (ahead for f ≥ 0, behind for f < 0). InverseLerp
                    // with equal bounds (softTip 0) returns 0 → a hard end, so softTip 0 keeps the streak's ends crisp.
                    float endEdge = f >= 0f
                        ? (f <= aheadStart ? 1f : 1f - Mathf.InverseLerp(aheadStart, ahead, f))
                        : (-f <= behindStart ? 1f : 1f - Mathf.InverseLerp(behindStart, behind, -f));
                    if (sideEdge <= 0.001f || endEdge <= 0.001f) continue;
                    if (fillSpatial)   // spatial fill → this pixel's (u,v) or canvas-anchored (Fixed); Solid/OverLife skip (constant, byte-identical)
                    {
                        float u = halfW > 1e-4f ? s / halfW : 0f;
                        float v = 2f * (f + behind) / len - 1f;   // f ∈ [-behind, ahead] → v ∈ [-1, 1] (len = behind + ahead)
                        col = EvalFill(fill, own, u, v, x, y, W, H);
                        cr = col.r; cg = col.g; cb = col.b;
                        if (brightMul != 1f) { cr = Mathf.Clamp01(cr * brightMul); cg = Mathf.Clamp01(cg * brightMul); cb = Mathf.Clamp01(cb * brightMul); }
                    }
                    float baseA = alpha * col.a * sideEdge * endEdge;
                    if (baseA <= 0.002f) continue;
                    if (mods.AnyPix)
                    {
                        Color pc = new Color(cr, cg, cb, 1f);
                        float pa = baseA;
                        float crossFrac = Mathf.Clamp01(as_ / Mathf.Max(0.001f, halfW));   // 0 = centre-line, 1 = side edge
                        if (!ApplyPix(mods.pix, ref pc, ref pa, x, y, wx, wy, frameIndex, crossFrac, own, pHash, W, H)) continue;
                        Over(buf, y * W + x, pc.r, pc.g, pc.b, pa);
                    }
                    else Over(buf, y * W + x, cr, cg, cb, baseA);
                }
        }

        // ── Text form (Gem-family extruded SDF letters) ──────────────────────────────
        // Per-font glyph-atlas snapshot: the raw SDF bytes + dims + padding, captured AFTER TryAddCharacters bakes
        // every needed glyph. A Dynamic SDF font REGENERATES its atlas when characters are added, so a snapshot
        // taken BEFORE the add would be stale — the real bug the prototype documents; hence snapshot-after. The
        // glyph bitmap content is a render INPUT (the same purity argument as the Sprite pixel cache), so memoising
        // it preserves same-inputs-same-output; it's re-baked/re-snapshotted when the font changes, the requested
        // string changes, or a lookup misses (e.g. after a domain reload nulls these statics). _textReady, set once
        // per frame in RenderFrame, is the gate every Text draw checks.
        static TMP_FontAsset _textFont;
        static byte[] _textSdf;
        static int _textAw, _textAh, _textPad;
        static string _textCachedChars = "";
        static bool _textReady;
#if UNITY_EDITOR
        static TMP_FontAsset _textAutoFont;   // cached auto-pick, so FindAssets isn't run every frame
#endif

        // Resolve the font (layer.textFont, else an auto-found readable one in the editor), bake every char of
        // layer.textString into its atlas, and snapshot the atlas into the static fields above. Returns false — so
        // the caller falls back to Disc per particle — when no font with a readable atlas exists.
        static bool EnsureTextGlyphs(PyrePlusLayer layer)
        {
            TMP_FontAsset font = layer.textFont;
#if UNITY_EDITOR
            if (font == null)
            {
                // Cache the auto-pick; re-find only if the cached one went invalid (a domain reload on an import
                // clears the static anyway, so a newly-added font is picked up then).
                if (_textAutoFont == null || _textAutoFont.atlasTexture == null || !_textAutoFont.atlasTexture.isReadable)
                    _textAutoFont = FindReadableTextFont();
                font = _textAutoFont;
            }
#endif
            if (font == null || font.atlasTexture == null || !font.atlasTexture.isReadable) return false;

            string need = layer.textString ?? "";
            bool needBake = font != _textFont || need != _textCachedChars || _textSdf == null;
            if (!needBake)
            {
                // A char present in the cached string set but somehow missing from the live lookup (font rebuilt,
                // atlas cleared) forces a fresh bake + snapshot.
                for (int i = 0; i < need.Length; i++)
                    if (!font.characterLookupTable.ContainsKey(need[i])) { needBake = true; break; }
            }
            if (needBake)
            {
                if (!string.IsNullOrEmpty(need)) font.TryAddCharacters(need);   // bake FIRST (regenerates the atlas)
                var atlas = font.atlasTexture;                                   // ...THEN snapshot the fresh atlas
                if (atlas == null || !atlas.isReadable) return false;
                _textAw = atlas.width; _textAh = atlas.height; _textPad = font.atlasPadding;
                _textSdf = atlas.GetRawTextureData<byte>().ToArray();
                _textFont = font;
                _textCachedChars = need;
            }
            return true;
        }

#if UNITY_EDITOR
        // The auto-pick: the first TMP_FontAsset in the project whose atlas is readable. Editor-only (AssetDatabase);
        // at runtime a null layer.textFont means the Text form falls back to Disc.
        static TMP_FontAsset FindReadableTextFont()
        {
            foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:TMP_FontAsset"))
            {
                var f = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UnityEditor.AssetDatabase.GUIDToAssetPath(g));
                if (f != null && f.atlasTexture != null && f.atlasTexture.isReadable) return f;
            }
            return null;
        }
#endif

        // Swarm-OFF line: lay the whole string out as one centred line and draw the letters far→near so nearer ones
        // correctly overdraw farther ones under a yaw (fixes the prototype's crude painter-skip occlusion). Each
        // char is particle index i on its OWN size/alpha/spin/tilt draws, but all share the blast life (there's no
        // swarm timing here). Called only when _textReady.
        static void RenderTextLine(Color32[] buf, int W, int H, float life, PyrePlusSpec spec, PyrePlusLayer layer,
                                   in ModSet mods, int frameIndex)
        {
            string ts = layer.textString ?? "";
            int n = ts.Length;
            if (n == 0) return;
            float spacing = Mathf.Clamp(layer.textSpacing, 0.6f, 1.6f);

            // Per-char layout: scale each glyph so its HEIGHT == its evaluated Size; advance by that glyph's own
            // scaled width plus a fixed tracking gap, all × Spacing (so a MinMax Size gives ragged letters that
            // still lay out cleanly). x0[i] = the char centre in TEXT space (0-centred); span = half the line
            // extent, used by the TextGradient fill. A missing glyph (space, unbaked) gets a blank monospace slot.
            var szA = new float[n];
            var glyphW = new float[n];
            var adv = new float[n];
            for (int i = 0; i < n; i++)
            {
                szA[i] = Mathf.Max(0f, Eval(layer.size, life, spec.seed, i, FldSize));
                float track = szA[i] * 0.28f;
                if (_textFont.characterLookupTable.TryGetValue(ts[i], out var tc) && tc.glyph.glyphRect.height > 0)
                {
                    var gr = tc.glyph.glyphRect;
                    glyphW[i] = gr.width * (szA[i] / gr.height);
                }
                else glyphW[i] = szA[i] * 0.4f;   // blank slot
                adv[i] = (glyphW[i] + track) * spacing;
            }
            float total = 0f;
            for (int i = 0; i < n; i++) total += adv[i];
            total -= szA[n - 1] * 0.28f * spacing;   // drop the trailing gap so the visual line centres
            var x0 = new float[n];
            float pen = -total * 0.5f;
            for (int i = 0; i < n; i++) { x0[i] = pen + glyphW[i] * 0.5f; pen += adv[i]; }
            float span = Mathf.Max(1f, total * 0.5f);

            float cx = W * 0.5f, cy = H * 0.5f;

            // Draw order: the letters share the same yaw sign (particleSpin at the blast life), so under a yaw one
            // side of every letter comes toward the viewer and the other recedes. AX.z at the representative yaw
            // tells us which: AX.z < 0 (yaw > 0) ⇒ each letter's +x edge recedes, so the near edge of the pair sits
            // on the RIGHT letter's left — paint left→right; AX.z > 0 ⇒ paint right→left. A per-char MinMax yaw
            // isn't perfectly ordered by one direction, but occlusion between wildly different rotations is
            // ambiguous anyway — this is a display nicety. No yaw ⇒ letters don't overlap in depth, order is moot.
            float yaw0 = Eval(layer.particleSpin, life, spec.seed, 0, FldSpin) * Mathf.Deg2Rad;
            float axz = -Mathf.Sin(yaw0) * Mathf.Cos(Eval(layer.gemTilt, life, spec.seed, 0, FldGemTilt) * Mathf.Deg2Rad);
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            float dir = axz > 0f ? -1f : 1f;
            System.Array.Sort(order, (a, b) => (x0[a] * dir).CompareTo(x0[b] * dir));

            for (int oi = 0; oi < n; oi++)
            {
                int i = order[oi];
                DrawTextChar(buf, W, H, cx + x0[i], cy, szA[i], life, spec, layer, i, ts[i], i, n,
                             x0[i], span, true, mods, frameIndex, 1f);
            }
        }

        // One extruded SDF character, centred at (cx, cy), rotated by its OWN yaw (particleSpin) + tilt (gemTilt)
        // about that centre. Per-pixel: walk depth layers front→back (layer 0 = the lit front face, deeper = darker
        // extrusion side), first SDF hit wins; bilinear SDF sample, threshold 0.5; a border band just inside the
        // edge; the fill / border colour comes from SampleTextColor. Geometry warps are intentionally skipped (the
        // 3D-family convention — same as the facet solids); PixelModifiers still run per lit pixel and Post passes
        // still hit the finished buffer. `sz` is the char HEIGHT in px, supplied by the caller (line layout or the
        // swarm particle) so it and the layout agree. lineMode/x0/span feed TextGradient; charIndex/n feed the
        // per-char step. brightMul is the swarm depth shade (1 in line mode).
        static void DrawTextChar(Color32[] buf, int W, int H, float cx, float cy, float sz, float own,
                                 PyrePlusSpec spec, PyrePlusLayer layer, int particleIndex, char ch, int charIndex, int n,
                                 float x0, float span, bool lineMode, in ModSet mods, int frameIndex, float brightMul,
                                 float orientDeg = 0f)
        {
            if (sz < 1.5f) return;
            if (!_textFont.characterLookupTable.TryGetValue(ch, out var tchar)) return;   // unbaked glyph (e.g. space)
            var gr = tchar.glyph.glyphRect;
            float gw = gr.width, gh = gr.height;
            if (gw < 1f || gh < 1f) return;

            float alphaEnv = Mathf.Clamp01(Eval(layer.alpha, own, spec.seed, particleIndex, FldAlpha));
            if (alphaEnv <= 0.002f) return;

            // Scale so the glyph HEIGHT == sz; halfW/halfH are the glyph box half-extents in screen px.
            float scGlyph = sz / gh;
            float halfW = gw * scGlyph * 0.5f, halfH = gh * scGlyph * 0.5f;

            // 3D extrusion: front face at +depth/2, back at -depth/2, walked in DepthSteps layers. !textSolid ⇒ a
            // single flat plane (depthSteps 0). Depth = a fraction of the char size, exactly like the prototype.
            bool solid = layer.textSolid;
            const int DepthSteps = 8;
            float depth = solid ? sz * Mathf.Clamp(layer.textDepth, 0.05f, 1f) : 0f;
            int steps = solid ? DepthSteps : 0;

            // Per-char rotation about its own centre: yaw about Y (particleSpin) then tilt about X (gemTilt), +z
            // toward the viewer — the facet solids' / prototype's Rot exactly. The char centre is the pivot, so
            // model space is centred at the origin and translated to (cx, cy) in screen space.
            float yaw = Eval(layer.particleSpin, own, spec.seed, particleIndex, FldSpin) * Mathf.Deg2Rad;
            float tilt = Eval(layer.gemTilt, own, spec.seed, particleIndex, FldGemTilt) * Mathf.Deg2Rad;
            float cyw = Mathf.Cos(yaw), syw = Mathf.Sin(yaw);
            float ct = Mathf.Cos(tilt), st = Mathf.Sin(tilt);
            // Swarm facing (S1): a screen-plane Z-ROLL applied AFTER yaw+tilt so letters follow the path readably
            // (default baseline +x; a tangent pointing up at a circle's east side ⇒ +90° roll ⇒ baseline up).
            // orientDeg 0 (swarm off / orient None) ⇒ doRoll false ⇒ Rot is byte-identical to the pre-S1 rotation.
            float roll = orientDeg * Mathf.Deg2Rad;
            bool doRoll = orientDeg != 0f;
            float cro = Mathf.Cos(roll), sro = Mathf.Sin(roll);
            Vector3 Rot(Vector3 p)
            {
                var q = new Vector3(p.x * cyw + p.z * syw, p.y, -p.x * syw + p.z * cyw);
                var r = new Vector3(q.x, q.y * ct - q.z * st, q.y * st + q.z * ct);
                if (doRoll) { float rx = r.x * cro - r.y * sro, ry = r.x * sro + r.y * cro; r = new Vector3(rx, ry, r.z); }
                return r;
            }
            Vector3 AX = Rot(new Vector3(1, 0, 0)), AY = Rot(new Vector3(0, 1, 0)), AZ = Rot(new Vector3(0, 0, 1));
            float axx = AX.x, axy = AX.y, ayx = AY.x, ayy = AY.y;
            float det = axx * ayy - axy * ayx;
            if (Mathf.Abs(det) < 1e-4f) return;   // face turned edge-on to the screen — nothing to raster
            float inv = 1f / det;

            // Raster bounds: project all 8 corners of the front+back box (centred coords), padded 2px.
            float minX = float.MaxValue, maxX = -float.MaxValue, minY = float.MaxValue, maxY = -float.MaxValue;
            for (int s = 0; s < 8; s++)
            {
                float uu = (s & 1) == 0 ? -halfW : halfW;
                float vv = (s & 2) == 0 ? -halfH : halfH;
                float zz = (s & 4) == 0 ? -depth * 0.5f : depth * 0.5f;
                float pxc = AX.x * uu + AY.x * vv + AZ.x * zz;
                float pyc = AX.y * uu + AY.y * vv + AZ.y * zz;
                if (pxc < minX) minX = pxc; if (pxc > maxX) maxX = pxc;
                if (pyc < minY) minY = pyc; if (pyc > maxY) maxY = pyc;
            }
            int px0 = Mathf.Max(0, Mathf.FloorToInt(cx + minX) - 2);
            int px1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + maxX) + 2);
            int py0 = Mathf.Max(0, Mathf.FloorToInt(cy + minY) - 2);
            int py1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + maxY) + 2);

            // Border band, converting the screen-px width to SDF VALUE units. TMP's normalized SDF rises ~0.5 (from
            // the 0.5 contour to fully-inside 1.0) over ~atlasPadding atlas px, i.e. slope ≈ 1/(2·pad) per atlas px.
            // A w-screen-px band is w/scGlyph atlas px deep (scGlyph = screen px per atlas px), so
            //   borderBand = (w / scGlyph) · (1/(2·pad)) = w / (2·pad·scGlyph).
            // This reproduces the prototype's hard-coded 0.10 at w≈1.5, pad≈9, scGlyph≈0.83 (0.10 ✓) — the factor 2
            // (absent from the design's shorthand "w/(pad·scale)") is that half-range-over-padding slope.
            float borderBand = layer.textBorderWidth > 0f
                ? layer.textBorderWidth / Mathf.Max(1e-3f, 2f * _textPad * scGlyph)
                : 0f;

            int pHash = Hash(spec.seed, particleIndex, FldModifier, 7 + _layerSalt);
            float boxExtent = Mathf.Max(halfW, halfH);

            for (int y = py0; y <= py1; y++)
                for (int x = px0; x <= px1; x++)
                {
                    float sxp = x + 0.5f - cx, syp = y + 0.5f - cy;
                    for (int k = 0; k <= steps; k++)
                    {
                        float zoff = solid ? depth * 0.5f - (depth * k) / DepthSteps : 0f;
                        // Solve C + AX·u + AY·v + AZ·zoff = (sxp, syp, *) in screen x/y (C == 0, centre at origin).
                        float rx = sxp - AZ.x * zoff;
                        float ry = syp - AZ.y * zoff;
                        float u = (rx * ayy - ry * ayx) * inv;
                        float v = (-rx * axy + ry * axx) * inv;
                        if (u < -halfW || u > halfW || v < -halfH || v > halfH) continue;

                        float sd = SampleTextSdf(gr, (u + halfW) / scGlyph, (v + halfH) / scGlyph);
                        if (sd < 0.5f) continue;

                        bool face = k == 0;
                        bool border = borderBand > 0f && sd < 0.5f + borderBand;
                        ZuiFill fill = border ? layer.textBorder : layer.textFill;
                        Color col = SampleTextColor(spec, layer, fill, charIndex, n, u, v, halfW, halfH, x0, span, lineMode, own, x, y, W, H);
                        if (!face)
                        {
                            // Extrusion side: fill (or border) × 0.45, then k-shaded darker with depth (prototype
                            // constants) so the sides read as receding.
                            col *= 0.45f * Mathf.Lerp(1f, 0.55f, k / (float)DepthSteps);
                        }

                        float rr = Mathf.Clamp01(col.r), gg = Mathf.Clamp01(col.g), bb = Mathf.Clamp01(col.b);
                        if (brightMul != 1f)
                        {
                            rr = Mathf.Clamp01(col.r * brightMul);
                            gg = Mathf.Clamp01(col.g * brightMul);
                            bb = Mathf.Clamp01(col.b * brightMul);
                        }
                        float pa = alphaEnv;
                        if (mods.AnyPix)
                        {
                            Color pc = new Color(rr, gg, bb, 1f);
                            float crossFrac = Mathf.Clamp01(Mathf.Sqrt(u * u + v * v) / Mathf.Max(0.001f, boxExtent));
                            if (!ApplyPix(mods.pix, ref pc, ref pa, x, y, x + 0.5f, y + 0.5f, frameIndex, crossFrac, own, pHash, W, H))
                                break;   // dropped by a modifier — the front face occludes any deeper layer anyway
                            Over(buf, y * W + x, pc.r, pc.g, pc.b, pa);
                        }
                        else Over(buf, y * W + x, rr, gg, bb, pa);
                        break;   // first (front-most) depth hit wins: face vs side
                    }
                }
        }

        // The Text fill/border colour at a surface point, from a ZuiFill. The renderer READS the fill three ways
        // (the fill's OWN Mode is deliberately NOT used as text scope — textFillMode is the scope):
        //   • a TEXTURE (texture != None) STAMPS the letters directly: sample fill.Evaluate(own, uN, vN) at the
        //     glyph point normalized to -1..1 across its box — textured / grid-lined / sprite-stamped letters;
        //   • a SOLID fill paints ONE flat colour (fill.color) — with PerCharStep that means every letter the same;
        //   • otherwise the fill's GRADIENT is swept per textFillMode below (PerCharGradient across each char's box
        //     along the angle axis, PerCharStep = grad(i/(n-1)) by index, TextGradient across the whole line).
        // The default text fill (OverLife, fire ramp, texture None) hits the last branch with the SAME gradient the
        // old textFillGradient carried, so default Text is byte-identical. (u,v) is the surface point in the char's
        // local px frame; halfW/halfH its box half-extents; x0 = the char centre in text space; span = half the
        // line extent; `own` the particle's life clock (only used by a texture's Evaluate).
        static Color SampleTextColor(PyrePlusSpec spec, PyrePlusLayer layer, ZuiFill fill, int charIndex, int n,
                                     float u, float v, float halfW, float halfH, float x0, float span, bool lineMode,
                                     float own, int px, int py, int W, int H)
        {
            if (fill == null) return Color.white;
            // A texture replaces the fill entirely — stamp it across the glyph box (normalized -1..1). This is the
            // ONLY spatial fill.Evaluate on the Text path (the gradient-sweep below reads fill.gradient with a
            // textFillMode scope, not a spatial mode), so it's the only one that honours Fixed space: Stamped =
            // glyph-local (byte-identical), Fixed = canvas-anchored via EvalFill.
            if (fill.texture != ZuiFill.TextureKind.None)
            {
                float uN = halfW > 1e-4f ? u / halfW : 0f;
                float vN = halfH > 1e-4f ? v / halfH : 0f;
                return EvalFill(fill, own, uN, vN, px, py, W, H);
            }
            // A Solid fill is one flat colour (with PerCharStep, every letter reads that same flat colour).
            if (fill.mode == ZuiFill.Mode.Solid) return fill.color;
            Gradient grad = fill.gradient;
            if (grad == null) return fill.color;
            float t;
            switch (layer.textFillMode)
            {
                case TextFillMode.PerCharStep:
                    t = n > 1 ? charIndex / (float)(n - 1) : 0f;
                    break;
                case TextFillMode.TextGradient:
                    if (lineMode)
                    {
                        float ang = layer.textGradientAngle * Mathf.Deg2Rad;
                        float tx = x0 + u, ty = v;
                        float along = tx * Mathf.Cos(ang) + ty * Mathf.Sin(ang);   // 0° = left→right across the line
                        t = span > 1e-3f ? Mathf.InverseLerp(-span, span, along) : 0.5f;
                    }
                    else t = n > 1 ? charIndex / (float)(n - 1) : 0f;   // swarm: no line → per-char step
                    break;
                default:   // PerCharGradient
                {
                    float ang = layer.textGradientAngle * Mathf.Deg2Rad;
                    float sa = Mathf.Sin(ang), ca = Mathf.Cos(ang);
                    float along = u * sa + v * ca;                       // 0° = v (vertical bottom→top)
                    float ext = Mathf.Abs(halfW * sa) + Mathf.Abs(halfH * ca);   // box half-extent along the axis
                    t = ext > 1e-3f ? Mathf.InverseLerp(-ext, ext, along) : 0.5f;
                    break;
                }
            }
            return grad.Evaluate(Mathf.Clamp01(t));
        }

        // Bilinear SDF sample from the snapshot atlas; (gx,gy) in glyph-local pixels (0..gr.width, 0..gr.height),
        // padding honoured so the border falloff just outside the tight rect is reachable. Ported verbatim from the
        // approved Text prototype (SampleSdf/At), reading the static snapshot instead of instance fields.
        static float SampleTextSdf(UnityEngine.TextCore.GlyphRect gr, float gx, float gy)
        {
            float ax = gr.x + gx, ay = gr.y + gy;
            ax = Mathf.Clamp(ax, gr.x - _textPad + 1, gr.x + gr.width + _textPad - 2);
            ay = Mathf.Clamp(ay, gr.y - _textPad + 1, gr.y + gr.height + _textPad - 2);
            int ix = Mathf.FloorToInt(ax), iy = Mathf.FloorToInt(ay);
            float fx = ax - ix, fy = ay - iy;
            float s00 = AtText(ix, iy), s10 = AtText(ix + 1, iy), s01 = AtText(ix, iy + 1), s11 = AtText(ix + 1, iy + 1);
            return Mathf.Lerp(Mathf.Lerp(s00, s10, fx), Mathf.Lerp(s01, s11, fx), fy);
        }

        static float AtText(int x, int y)
        {
            if (x < 0 || y < 0 || x >= _textAw || y >= _textAh) return 0f;
            return _textSdf[y * _textAw + x] / 255f;
        }

        // ── 3D facet solids (Gem / Box / Pyramid / Can) ──────────────────────────────
        // ONE renderer for every true-3D convex facet solid, generalized out of the approved gem prototype
        // (Diamond3DProbe). It differs per FORM only in (model verts, faces, which edges draw as lines) — supplied
        // by the Build*Geometry helpers below; EVERYTHING else (rotation, backface cull, per-pixel point light +
        // Blinn-Phong, hard edge lines, halo + inner glow, ApplyPix, Over) is shared and unchanged. Convex ⇒
        // backface culling ONLY (no depth sort). Determinism unchanged: pure static math through Eval/Hash. Every
        // pixel composites into the swarm buffer through Over.
        //
        // GEM BYTE-IDENTITY: BuildGemGeometry reproduces the prototype's model/faces verbatim and passes
        // lineEdges == null (every edge is a line — the gem's original behaviour), so a Gem renders exactly as the
        // old DrawGem did (the orchestrator hash-gates Gem). Box/Pyramid/Can supply their own verts/faces and an
        // explicit line-edge set that excludes quad diagonals / barrel seams / cap spokes.
        //
        // Signature extends the design's listed one with `in ModSet mods, int frameIndex` because the design's own
        // body mandates running the PixelModifiers per lit pixel (which need them). GEOMETRY modifiers are
        // deliberately NOT applied to the solids in this slice — a 3D-consistent warp fold is its own problem; post
        // modifiers still hit the finished buffer later in ApplyPost, unchanged.
        static void DrawFacetSolid(Color32[] buf, int W, int H, float cx, float cy, float own,
                            PyrePlusSpec spec, PyrePlusLayer layer, int particleIndex, in ModSet mods, int frameIndex,
                            float sizeMul, float brightMul, float orientDeg = 0f)
        {
            // R = the size envelope (exactly as Disc evaluates it) × the depth size multiplier. Skip sub-pixel gems.
            float R = Mathf.Max(0f, Eval(layer.size, own, spec.seed, particleIndex, FldSize)) * sizeMul;
            if (R < 1.5f) return;

            // Output alpha rides the particle's own alpha envelope (multiplies EVERY emitted gem pixel — body,
            // line and glow). Early-out on a fully-faded particle, respecting the same a<=0.002 skip as Disc.
            float alphaEnv = Mathf.Clamp01(Eval(layer.alpha, own, spec.seed, particleIndex, FldAlpha));
            if (alphaEnv <= 0.002f) return;

            // Shared per-particle travel (T7): the gem follows the same own-life travel path a disc does. Guarded
            // to an exact no-op when both are Static 0 (a default gem is unaffected). Not in the design's listed
            // pipeline, but the field is shared spec data and a travelling gem is the correct behaviour.
            if (!(IsStaticZero(layer.particlePathX) && IsStaticZero(layer.particlePathY)))
            {
                cx += Eval(layer.particlePathX, own, spec.seed, particleIndex, FldPathX);
                cy += Eval(layer.particlePathY, own, spec.seed, particleIndex, FldPathY);
            }

            // Model (y up, +z toward the viewer after rotation), triangle faces, and the LINE-edge set — all three
            // supplied per form. lineEdges == null means "every edge is a line" (Gem). See the Build*Geometry
            // helpers at the bottom of the class.
            Vector3[] model;
            int[][] faces;
            HashSet<int> lineEdges;
            switch (layer.shapeForm)
            {
                case ShapeForm.Box:     BuildBoxGeometry(layer, R, out model, out faces, out lineEdges); break;
                case ShapeForm.Pyramid: BuildPyramidGeometry(layer, R, out model, out faces, out lineEdges); break;
                case ShapeForm.Can:     BuildCanGeometry(layer, R, out model, out faces, out lineEdges); break;
                default:                BuildGemGeometry(layer, R, out model, out faces, out lineEdges); break;
            }

            // Rotation (R2 full 3D): ROLL about Z in model space FIRST (Turn/Tilt come after), then yaw about Y
            // (the solid's 3D yaw = particleSpin/Turn on its own life), then the world tilt about X (gemTilt on its
            // own life) — the prototype's Rot with a roll prepended, +z toward the viewer. The roll block is guarded
            // on roll != 0f so a default solid (roll Static 0) is byte-identical to the pre-R2 Rot.
            float yaw = Eval(layer.particleSpin, own, spec.seed, particleIndex, FldSpin) * Mathf.Deg2Rad;
            float tilt = Eval(layer.gemTilt, own, spec.seed, particleIndex, FldGemTilt) * Mathf.Deg2Rad;
            // Swarm facing (S1) adds to the ROLL (per the design: "Solids: add to roll"). orientDeg 0 (swarm off /
            // orient None) ⇒ rollDeg is exactly the gemRoll Eval ⇒ byte-identical (doRoll gate unchanged).
            float rollDeg = Eval(layer.gemRoll, own, spec.seed, particleIndex, FldGemRoll);
            if (orientDeg != 0f) rollDeg += orientDeg;
            float roll = rollDeg * Mathf.Deg2Rad;
            float cyw = Mathf.Cos(yaw), syw = Mathf.Sin(yaw);
            float ct = Mathf.Cos(tilt), st = Mathf.Sin(tilt);
            bool doRoll = roll != 0f;
            float cro = Mathf.Cos(roll), sro = Mathf.Sin(roll);
            Vector3 Rot(Vector3 p)
            {
                if (doRoll) { float rx = p.x * cro - p.y * sro, ry = p.x * sro + p.y * cro; p = new Vector3(rx, ry, p.z); }
                var q = new Vector3(p.x * cyw + p.z * syw, p.y, -p.x * syw + p.z * cyw);
                return new Vector3(q.x, q.y * ct - q.z * st, q.y * st + q.z * ct);
            }
            var verts = new Vector3[model.Length];
            for (int k = 0; k < model.Length; k++) verts[k] = Rot(model[k]);

            // Visible faces: outward normal (flip vs centroid so the origin-inside-the-solid convention holds),
            // then backface cull n.z <= 0. Convex + culled ⇒ front faces tile the silhouette with no overlap.
            var visA = new Vector2[faces.Length];   // screen triangle (centered coords: vertex.xy)
            var visB = new Vector2[faces.Length];
            var visC = new Vector2[faces.Length];
            var visP0 = new Vector3[faces.Length];  // the three 3D verts, for the barycentric world point
            var visP1 = new Vector3[faces.Length];
            var visP2 = new Vector3[faces.Length];
            var visNrm = new Vector3[faces.Length];
            int visCount = 0;
            // Unique visible LINE edges (screen segments) for the hard line pass, deduped by vertex pair. Sized to
            // the loose upper bound of 3 edges per face; only the line edges are actually stored.
            var edgeA = new Vector2[3 * faces.Length];
            var edgeB = new Vector2[3 * faces.Length];
            int edgeCount = 0;
            var seenEdge = new HashSet<int>();
            for (int fi = 0; fi < faces.Length; fi++)
            {
                var f = faces[fi];
                Vector3 p0 = verts[f[0]], p1 = verts[f[1]], p2 = verts[f[2]];
                Vector3 nrm = Vector3.Cross(p1 - p0, p2 - p0);
                Vector3 centroid = (p0 + p1 + p2) / 3f;
                if (Vector3.Dot(nrm, centroid) < 0f) nrm = -nrm;   // force outward (origin is inside the solid)
                if (nrm.z <= 0f) continue;                          // backface: not seen
                nrm = nrm.normalized;
                visA[visCount] = new Vector2(p0.x, p0.y);
                visB[visCount] = new Vector2(p1.x, p1.y);
                visC[visCount] = new Vector2(p2.x, p2.y);
                visP0[visCount] = p0; visP1[visCount] = p1; visP2[visCount] = p2;
                visNrm[visCount] = nrm;
                visCount++;
                for (int e = 0; e < 3; e++)
                {
                    int i0 = f[e], i1 = f[(e + 1) % 3];
                    int key = i0 < i1 ? i0 * 64 + i1 : i1 * 64 + i0;
                    if (!seenEdge.Add(key)) continue;
                    // lineEdges == null ⇒ every edge is a line (Gem's original behaviour, byte-identical). A non-
                    // null set names the LINE edges only, so quad diagonals / barrel seams / cap spokes are skipped
                    // — they neither draw a hard line nor seed the inner glow, which measures distance to this same
                    // edge set (so faces glow from their real borders, not phantom internal diagonals).
                    if (lineEdges != null && !lineEdges.Contains(key)) continue;
                    edgeA[edgeCount] = new Vector2(verts[i0].x, verts[i0].y);
                    edgeB[edgeCount] = new Vector2(verts[i1].x, verts[i1].y);
                    edgeCount++;
                }
            }
            if (visCount == 0) return;

            // Light POSITION from the gem's light angles at distance gemLightDistance·R; the falloff range is
            // (gemLightDistance + 1.2)·R, so it tracks the distance (a far light still reaches the solid). At the
            // default distance 3.5 this is exactly 3.5·R / 4.7·R (the prototype's proportions — 3.5 + 1.2 == 4.7 in
            // float, byte-identical). Up-left-front for the default -55°/38° angles.
            float lyaw = layer.gemLightYaw * Mathf.Deg2Rad, lpitch = layer.gemLightPitch * Mathf.Deg2Rad;
            float ldist = layer.gemLightDistance * R, lhoriz = ldist * Mathf.Cos(lpitch);
            Vector3 lightPos = new Vector3(lhoriz * Mathf.Sin(lyaw), ldist * Mathf.Sin(lpitch), lhoriz * Mathf.Cos(lyaw));
            float lrange = (layer.gemLightDistance + 1.2f) * R, lrange2 = lrange * lrange;
            Vector3 viewDir = new Vector3(0f, 0f, 1f);
            // Colours are now authorable ZuiFills (Part A). Hoisted at (own, 0, 0): for the default Solid slots (and
            // an OverLife material) this is a constant the whole raster reuses, byte-identical to the old renderer
            // constants / colorOverLife path. A SPATIAL fill (Linear/Radial/Noise) recomputes per pixel at the
            // pixel's particle-local (lx/R, ly/R) below. NOTE: the halo (edge glow) tint used to reuse the line
            // colour; it is split into gemEdgeGlowFill — byte-identical while gemLineFill sits at its default, whose
            // colour equals gemEdgeGlowFill's default.
            var specFill = layer.gemSpecularFill; bool specSpatial = IsSpatialFill(specFill);
            var innerFill = layer.gemInnerGlowFill; bool innerSpatial = IsSpatialFill(innerFill);
            var lineFill = layer.gemLineFill; bool lineSpatial = IsSpatialFill(lineFill);
            var edgeFill = layer.gemEdgeGlowFill; bool edgeSpatial = IsSpatialFill(edgeFill);
            var matFill = layer.shapeFill;   // material fill; baseCol is evaluated per pixel below (u,v ignored for Solid/OverLife → byte-identical)
            Color specColor = specFill != null ? specFill.Evaluate(own, 0f, 0f) : Color.white;    // Blinn-Phong highlight tint
            Color innerColor = innerFill != null ? innerFill.Evaluate(own, 0f, 0f) : Color.white; // inner-glow tint
            Color lineColor = lineFill != null ? lineFill.Evaluate(own, 0f, 0f) : Color.white;    // hard edge-line colour
            Color edgeGlowColor = edgeFill != null ? edgeFill.Evaluate(own, 0f, 0f) : Color.white; // halo/edge-glow tint
            float lineW = layer.gemLineWidth;
            float ambient = layer.gemAmbient, specStr = layer.gemSpecular;
            float diffuse = layer.gemDiffuse, specPow = layer.gemSpecPower;   // P5: the DIFFUSE strength (was hardcoded 2.1) + specular exponent (was hardcoded 48)

            // The two glow strengths on the particle's own life (0..1). Halo radius / inner radius scale with R
            // (prototype constants 7 and 9 at R=30 → 0.24·R and 0.30·R), floored so tiny gems still glow.
            float edgeGlow = Mathf.Clamp01(Eval(layer.gemEdgeGlow, own, spec.seed, particleIndex, FldGemEdgeGlow));
            float innerGlow = Mathf.Clamp01(Eval(layer.gemInnerGlow, own, spec.seed, particleIndex, FldGemInnerGlow));
            float haloR = Mathf.Max(2.5f, 0.24f * R);
            float innerR = Mathf.Max(3f, 0.30f * R);

            // Raster bounds: the screen bounding box of the rotated verts (centered coords), EXPANDED by haloR so
            // the outside halo isn't clipped, then to absolute pixels around (cx, cy).
            float minX = float.MaxValue, maxX = -float.MaxValue, minY = float.MaxValue, maxY = -float.MaxValue;
            for (int k = 0; k < verts.Length; k++)
            {
                if (verts[k].x < minX) minX = verts[k].x;
                if (verts[k].x > maxX) maxX = verts[k].x;
                if (verts[k].y < minY) minY = verts[k].y;
                if (verts[k].y > maxY) maxY = verts[k].y;
            }
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx + minX - haloR));
            int x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + maxX + haloR));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy + minY - haloR));
            int y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + maxY + haloR));

            int pHash = Hash(spec.seed, particleIndex, FldModifier, 7 + _layerSalt);   // stable per-particle seed for PixelInfo.hash

            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    // Centered pixel coords (relative to the gem centre) — the space the model verts live in.
                    float lx = x + 0.5f - cx, ly = y + 0.5f - cy;
                    Vector2 p = new Vector2(lx, ly);

                    // Spatial fills: this pixel's particle-local point (lx/R, ly/R) in -1..1 space (Stamped), or the
                    // canvas-anchored point (Fixed), via EvalFill per fill's own space. Ignored by the default Solid/
                    // OverLife fills, so these recomputes stay skipped and the raster is byte-identical.
                    float u = lx / R, v = ly / R;
                    if (lineSpatial) lineColor = EvalFill(lineFill, own, u, v, x, y, W, H);
                    if (edgeSpatial) edgeGlowColor = EvalFill(edgeFill, own, u, v, x, y, W, H);
                    if (innerSpatial) innerColor = EvalFill(innerFill, own, u, v, x, y, W, H);
                    if (specSpatial) specColor = EvalFill(specFill, own, u, v, x, y, W, H);

                    // Nearest visible edge distance — drives BOTH glows and the hard line test, inside and out.
                    float edist = float.MaxValue;
                    for (int e = 0; e < edgeCount; e++)
                        edist = Mathf.Min(edist, DistSeg(p, edgeA[e], edgeB[e]));

                    bool hasFace = false, isLine = false;
                    float fr = 0f, fg = 0f, fb = 0f;   // face RGB before glows
                    for (int vi = 0; vi < visCount; vi++)
                    {
                        if (!InTri(p, visA[vi], visB[vi], visC[vi], out float w0, out float w1, out float w2)) continue;
                        hasFace = true;
                        Vector3 P = visP0[vi] * w0 + visP1[vi] * w1 + visP2[vi] * w2;
                        Vector3 toL = lightPos - P;
                        float dist = toL.magnitude;
                        Vector3 L = toL / Mathf.Max(1e-4f, dist);
                        float atten = 1f / (1f + dist * dist / lrange2);
                        float ndl = Mathf.Max(0f, Vector3.Dot(visNrm[vi], L));
                        float lit = ambient + diffuse * ndl * atten;
                        if (edist <= lineW)
                        {
                            isLine = true;
                            float k = Mathf.Clamp(0.25f + lit, 0f, 1.15f);   // edges catch the light: bright lit, dim shadowed
                            fr = lineColor.r * k; fg = lineColor.g * k; fb = lineColor.b * k;
                        }
                        else
                        {
                            Vector3 Hh = (L + viewDir).normalized;
                            float sp = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(visNrm[vi], Hh)), specPow);
                            Color baseCol = matFill != null ? EvalFill(matFill, own, u, v, x, y, W, H) : Color.white;
                            float sAdd = specStr * sp * atten;
                            fr = baseCol.r * lit + specColor.r * sAdd;
                            fg = baseCol.g * lit + specColor.g * sAdd;
                            fb = baseCol.b * lit + specColor.b * sAdd;
                        }
                        break;   // convex + culled: first hit wins
                    }

                    // Halo (glow level 1): a soft additive halo around every edge line, inside AND out.
                    float halo = Mathf.Pow(Mathf.Max(0f, 1f - edist / haloR), 2f);
                    float haloAmt = 0.85f * edgeGlow * halo;

                    if (hasFace)
                    {
                        // Add the halo (both) and the inner glow (facet interiors, non-line) to the face RGB.
                        fr += edgeGlowColor.r * haloAmt; fg += edgeGlowColor.g * haloAmt; fb += edgeGlowColor.b * haloAmt;
                        if (!isLine)
                        {
                            float core = Mathf.Pow(Mathf.Clamp01(edist / innerR), 1.4f);
                            float innerAmt = 0.75f * innerGlow * core;
                            fr += innerColor.r * innerAmt; fg += innerColor.g * innerAmt; fb += innerColor.b * innerAmt;
                        }
                        // brightMul scales the lit RGB (depth shading), clamped per channel like Disc.
                        float rr = Mathf.Clamp01(fr * brightMul), gg = Mathf.Clamp01(fg * brightMul), bb = Mathf.Clamp01(fb * brightMul);
                        float pa = alphaEnv;
                        if (mods.AnyPix)
                        {
                            Color pc = new Color(rr, gg, bb, 1f);
                            float crossFrac = Mathf.Clamp01(Mathf.Sqrt(lx * lx + ly * ly) / Mathf.Max(0.001f, R));
                            if (!ApplyPix(mods.pix, ref pc, ref pa, x, y, x + 0.5f, y + 0.5f, frameIndex, crossFrac, own, pHash, W, H)) continue;
                            Over(buf, y * W + x, pc.r, pc.g, pc.b, pa);
                        }
                        else Over(buf, y * W + x, rr, gg, bb, pa);
                    }
                    else
                    {
                        // Outside-halo glow fragment: a soft additive glow spilling past the silhouette. Its colour
                        // is the halo tint; its ALPHA is the halo strength × the particle alpha, so it reads as a
                        // soft glow when composited through Over.
                        float pa = Mathf.Clamp01(haloAmt) * alphaEnv;
                        if (pa <= 0.002f) continue;
                        float rr = Mathf.Clamp01(edgeGlowColor.r * brightMul), gg = Mathf.Clamp01(edgeGlowColor.g * brightMul), bb = Mathf.Clamp01(edgeGlowColor.b * brightMul);
                        if (mods.AnyPix)
                        {
                            Color pc = new Color(rr, gg, bb, 1f);
                            float crossFrac = Mathf.Clamp01(Mathf.Sqrt(lx * lx + ly * ly) / Mathf.Max(0.001f, R));
                            if (!ApplyPix(mods.pix, ref pc, ref pa, x, y, x + 0.5f, y + 0.5f, frameIndex, crossFrac, own, pHash, W, H)) continue;
                            Over(buf, y * W + x, pc.r, pc.g, pc.b, pa);
                        }
                        else Over(buf, y * W + x, rr, gg, bb, pa);
                    }
                }
        }

        // ── Orb: a true lit sphere, analytic (no facets) ─────────────────────────────────────────────────────
        // Silhouette = the plain circle d ≤ R, NEVER squashed by spin/tilt (a sphere looks identical from every
        // angle). The per-pixel visible surface point is P = (dx, dy, +sqrt(R²−dx²−dy²)) in screen space (+z toward
        // the viewer), normal N = P/R — so the smooth silhouette falloff comes out for free. The lighting formula,
        // edge line, halo and inner glow are IDENTICAL to DrawFacetSolid's face path (mirrored here rather than
        // shared, so the facet forms stay byte-identical): the only differences are (1) N is analytic instead of a
        // flat face normal, and (2) how spin/tilt are applied — see the lighting-frame rotation below.
        static void DrawOrb(Color32[] buf, int W, int H, float cx, float cy, float own,
                            PyrePlusSpec spec, PyrePlusLayer layer, int particleIndex, in ModSet mods, int frameIndex,
                            float sizeMul, float brightMul, float orientDeg = 0f)
        {
            float R = Mathf.Max(0f, Eval(layer.size, own, spec.seed, particleIndex, FldSize)) * sizeMul;
            if (R < 1.5f) return;
            float alphaEnv = Mathf.Clamp01(Eval(layer.alpha, own, spec.seed, particleIndex, FldAlpha));
            if (alphaEnv <= 0.002f) return;

            // Shared per-particle travel (T7) — same guard as the facet/disc path (exact no-op at Static 0).
            if (!(IsStaticZero(layer.particlePathX) && IsStaticZero(layer.particlePathY)))
            {
                cx += Eval(layer.particlePathX, own, spec.seed, particleIndex, FldPathX);
                cy += Eval(layer.particlePathY, own, spec.seed, particleIndex, FldPathY);
            }

            // Light POSITION exactly as DrawFacetSolid builds it (screen space, distance gemLightDistance·R, falloff
            // (gemLightDistance + 1.2)·R; default 3.5 → 3.5·R / 4.7·R).
            float lyaw = layer.gemLightYaw * Mathf.Deg2Rad, lpitch = layer.gemLightPitch * Mathf.Deg2Rad;
            float ldist = layer.gemLightDistance * R, lhoriz = ldist * Mathf.Cos(lpitch);
            Vector3 lightPos = new Vector3(lhoriz * Mathf.Sin(lyaw), ldist * Mathf.Sin(lpitch), lhoriz * Mathf.Cos(lyaw));
            float lrange = (layer.gemLightDistance + 1.2f) * R, lrange2 = lrange * lrange;
            Vector3 viewDir = new Vector3(0f, 0f, 1f);

            // ── Orb spin/tilt = a LIGHTING-FRAME rotation, NOT a geometry rotation. A uniform sphere's silhouette
            //    AND its true screen-space normals (N = P/R) are both rotation-invariant, so a plain lit sphere
            //    would show NOTHING as it spins. To roll the shading + specular hotspot around the ball (matching
            //    the Bakery Orb, which spins in the sphere-local shading plane), we rotate the LIGHT by the INVERSE
            //    of the sphere's rotation and keep shading with the true N = P/R (which preserves the correct rim
            //    falloff). This is EXACTLY equivalent to rotating N (and P) FORWARD by the rotation with a fixed
            //    light: a rotation preserves dot products and lengths, so N·(Rot⁻¹·L) == (Rot·N)·L and
            //    |Rot⁻¹·L − P| == |L − Rot·P| — same lit value, same specular, same attenuation. Rotating the light
            //    ONCE here is far cheaper than rotating N per pixel. Rot (the facet solids' own) is yaw-about-Y then
            //    tilt-about-X; its inverse is yaw(−) applied after tilt(−).
            float yaw = Eval(layer.particleSpin, own, spec.seed, particleIndex, FldSpin) * Mathf.Deg2Rad;
            float tilt = Eval(layer.gemTilt, own, spec.seed, particleIndex, FldGemTilt) * Mathf.Deg2Rad;
            // Swarm facing (S1) adds to the ROLL (the Orb rolls its lit hotspot around the ball). orientDeg 0
            // (swarm off / orient None) ⇒ rollDeg is exactly the gemRoll Eval ⇒ byte-identical.
            float rollDeg = Eval(layer.gemRoll, own, spec.seed, particleIndex, FldGemRoll);
            if (orientDeg != 0f) rollDeg += orientDeg;
            float roll = rollDeg * Mathf.Deg2Rad;
            float cyw = Mathf.Cos(yaw), syw = Mathf.Sin(yaw);
            float ct = Mathf.Cos(tilt), st = Mathf.Sin(tilt);
            {
                // Roll the LIGHT by the INVERSE of the sphere's rotation (R2). The forward model rotation is
                // ROLL(Z)→YAW(Y)→TILT(X), so its inverse is TILT(−X)→YAW(−Y)→ROLL(−Z), applied to the light in
                // that order. TiltX(−tilt): undo the tilt about X, then YawY(−yaw): undo the yaw about Y.
                float ax = lightPos.x;
                float ay = lightPos.y * ct + lightPos.z * st;
                float az = -lightPos.y * st + lightPos.z * ct;
                lightPos = new Vector3(ax * cyw - az * syw, ay, ax * syw + az * cyw);
                // ROLL(−Z): undo the model-space roll last (guarded so roll==0 is byte-identical — the hotspot
                // rolls around the ball's centre as roll animates).
                if (roll != 0f)
                {
                    float cro = Mathf.Cos(roll), sro = Mathf.Sin(roll);
                    float rx = lightPos.x * cro + lightPos.y * sro;
                    float ry = -lightPos.x * sro + lightPos.y * cro;
                    lightPos = new Vector3(rx, ry, lightPos.z);
                }
            }

            var specFill = layer.gemSpecularFill; bool specSpatial = IsSpatialFill(specFill);
            var innerFill = layer.gemInnerGlowFill; bool innerSpatial = IsSpatialFill(innerFill);
            var lineFill = layer.gemLineFill; bool lineSpatial = IsSpatialFill(lineFill);
            var edgeFill = layer.gemEdgeGlowFill; bool edgeSpatial = IsSpatialFill(edgeFill);
            var matFill = layer.shapeFill; bool matSpatial = IsSpatialFill(matFill);
            Color specColor = specFill != null ? specFill.Evaluate(own, 0f, 0f) : Color.white;
            Color innerColor = innerFill != null ? innerFill.Evaluate(own, 0f, 0f) : Color.white;
            Color lineColor = lineFill != null ? lineFill.Evaluate(own, 0f, 0f) : Color.white;
            Color edgeGlowColor = edgeFill != null ? edgeFill.Evaluate(own, 0f, 0f) : Color.white;
            float lineW = layer.gemLineWidth;
            float ambient = layer.gemAmbient, specStr = layer.gemSpecular;
            float diffuse = layer.gemDiffuse, specPow = layer.gemSpecPower;   // P5: the DIFFUSE strength (was hardcoded 2.1) + specular exponent (was hardcoded 48)

            float edgeGlow = Mathf.Clamp01(Eval(layer.gemEdgeGlow, own, spec.seed, particleIndex, FldGemEdgeGlow));
            float innerGlow = Mathf.Clamp01(Eval(layer.gemInnerGlow, own, spec.seed, particleIndex, FldGemInnerGlow));
            float haloR = Mathf.Max(2.5f, 0.24f * R);
            float innerR = Mathf.Max(3f, 0.30f * R);
            // Hoisted material fill (default OverLife = gradient.Evaluate(own), byte-identical); a spatial material
            // recomputes per pixel at (lx/R, ly/R) below.
            Color baseCol = matFill != null ? matFill.Evaluate(own, 0f, 0f) : Color.white;

            // Raster bounds: the circle d ≤ R, expanded by haloR so the outside halo isn't clipped.
            float extent = R + haloR;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - extent));
            int x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + extent));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - extent));
            int y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + extent));
            int pHash = Hash(spec.seed, particleIndex, FldModifier, 7 + _layerSalt);

            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float lx = x + 0.5f - cx, ly = y + 0.5f - cy;
                    // Spatial fills at this pixel's particle-local point (lx/R, ly/R) — or canvas-anchored (Fixed),
                    // per fill via EvalFill; constant fills skip (byte-identical).
                    float fu = lx / R, fv = ly / R;
                    if (lineSpatial) lineColor = EvalFill(lineFill, own, fu, fv, x, y, W, H);
                    if (edgeSpatial) edgeGlowColor = EvalFill(edgeFill, own, fu, fv, x, y, W, H);
                    if (innerSpatial) innerColor = EvalFill(innerFill, own, fu, fv, x, y, W, H);
                    if (specSpatial) specColor = EvalFill(specFill, own, fu, fv, x, y, W, H);
                    if (matSpatial) baseCol = EvalFill(matFill, own, fu, fv, x, y, W, H);
                    float d = Mathf.Sqrt(lx * lx + ly * ly);
                    // The orb's ONLY edge is the silhouette rim; distance to it (both sides) drives lines + glows,
                    // exactly as DrawFacetSolid's `edist` (nearest face-edge distance) does.
                    float rimDist = Mathf.Abs(d - R);

                    bool hasFace = d <= R;
                    bool isLine = false;
                    float fr = 0f, fg = 0f, fb = 0f;
                    if (hasFace)
                    {
                        float z = Mathf.Sqrt(Mathf.Max(0f, R * R - d * d));   // toward-viewer bulge
                        Vector3 P = new Vector3(lx, ly, z);
                        Vector3 N = P / R;
                        Vector3 toL = lightPos - P;
                        float dist = toL.magnitude;
                        Vector3 L = toL / Mathf.Max(1e-4f, dist);
                        float atten = 1f / (1f + dist * dist / lrange2);
                        float ndl = Mathf.Max(0f, Vector3.Dot(N, L));
                        float lit = ambient + diffuse * ndl * atten;
                        if (rimDist <= lineW)   // inside points at the rim draw the silhouette line, lit-scaled
                        {
                            isLine = true;
                            float k = Mathf.Clamp(0.25f + lit, 0f, 1.15f);
                            fr = lineColor.r * k; fg = lineColor.g * k; fb = lineColor.b * k;
                        }
                        else
                        {
                            Vector3 Hh = (L + viewDir).normalized;
                            float sp = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(N, Hh)), specPow);
                            float sAdd = specStr * sp * atten;
                            fr = baseCol.r * lit + specColor.r * sAdd;
                            fg = baseCol.g * lit + specColor.g * sAdd;
                            fb = baseCol.b * lit + specColor.b * sAdd;
                        }
                    }

                    float halo = Mathf.Pow(Mathf.Max(0f, 1f - rimDist / haloR), 2f);
                    float haloAmt = 0.85f * edgeGlow * halo;

                    if (hasFace)
                    {
                        fr += edgeGlowColor.r * haloAmt; fg += edgeGlowColor.g * haloAmt; fb += edgeGlowColor.b * haloAmt;
                        if (!isLine)
                        {
                            // Inner glow rising from the interior: measured inward from the rim (R − d == rimDist
                            // inside), so it's 0 at the silhouette and grows toward the centre — same shape as the
                            // facet's `edist/innerR` core.
                            float core = Mathf.Pow(Mathf.Clamp01(rimDist / innerR), 1.4f);
                            float innerAmt = 0.75f * innerGlow * core;
                            fr += innerColor.r * innerAmt; fg += innerColor.g * innerAmt; fb += innerColor.b * innerAmt;
                        }
                        float rr = Mathf.Clamp01(fr * brightMul), gg = Mathf.Clamp01(fg * brightMul), bb = Mathf.Clamp01(fb * brightMul);
                        float pa = alphaEnv;
                        if (mods.AnyPix)
                        {
                            Color pc = new Color(rr, gg, bb, 1f);
                            float crossFrac = Mathf.Clamp01(d / Mathf.Max(0.001f, R));
                            if (!ApplyPix(mods.pix, ref pc, ref pa, x, y, x + 0.5f, y + 0.5f, frameIndex, crossFrac, own, pHash, W, H)) continue;
                            Over(buf, y * W + x, pc.r, pc.g, pc.b, pa);
                        }
                        else Over(buf, y * W + x, rr, gg, bb, pa);
                    }
                    else
                    {
                        // Outside-halo fragment (past the silhouette) — same as DrawFacetSolid's else branch.
                        float pa = Mathf.Clamp01(haloAmt) * alphaEnv;
                        if (pa <= 0.002f) continue;
                        float rr = Mathf.Clamp01(edgeGlowColor.r * brightMul), gg = Mathf.Clamp01(edgeGlowColor.g * brightMul), bb = Mathf.Clamp01(edgeGlowColor.b * brightMul);
                        if (mods.AnyPix)
                        {
                            Color pc = new Color(rr, gg, bb, 1f);
                            float crossFrac = Mathf.Clamp01(d / Mathf.Max(0.001f, R));
                            if (!ApplyPix(mods.pix, ref pc, ref pa, x, y, x + 0.5f, y + 0.5f, frameIndex, crossFrac, own, pHash, W, H)) continue;
                            Over(buf, y * W + x, pc.r, pc.g, pc.b, pa);
                        }
                        else Over(buf, y * W + x, rr, gg, bb, pa);
                    }
                }
        }

        // ── Ring: a flat two-sided tilted annulus (a Saturn ring), analytic ──────────────────────────────────
        // The ring lies in its own LOCAL XY plane (normal +Z) at radius ρ ∈ [innerR, R], transformed to screen by
        // the facet solids' own Rot (yaw = particleSpin, tilt = gemTilt). Under that map, with spin 0, a screen
        // pixel (lx, ly) comes from local (u, v) = (lx, ly/cos·tilt) — i.e. the ellipse is the circle compressed
        // vertically by cos(tilt) — EXACTLY Bakery's RingWithTilt, which is why the edge-on degeneracy is |cos
        // tilt| ≈ 0 (tilt → 90°). Spin adds an in-plane roll (a shear/rotation of the ellipse). It's TWO-SIDED
        // (no backface cull): the plane normal is flipped to whichever side faces the viewer (N.z > 0). Lighting /
        // lines / glows mirror DrawFacetSolid, with the flat plane's CONSTANT normal and a per-pixel 3D plane point.
        static void DrawRing(Color32[] buf, int W, int H, float cx, float cy, float own,
                             PyrePlusSpec spec, PyrePlusLayer layer, int particleIndex, in ModSet mods, int frameIndex,
                             float sizeMul, float brightMul)
        {
            float R = Mathf.Max(0f, Eval(layer.size, own, spec.seed, particleIndex, FldSize)) * sizeMul;
            if (R < 1.5f) return;
            float alphaEnv = Mathf.Clamp01(Eval(layer.alpha, own, spec.seed, particleIndex, FldAlpha));
            if (alphaEnv <= 0.002f) return;
            float innerR = R * Mathf.Clamp(layer.ringInner, 0.1f, 0.92f);   // the hole radius

            if (!(IsStaticZero(layer.particlePathX) && IsStaticZero(layer.particlePathY)))
            {
                cx += Eval(layer.particlePathX, own, spec.seed, particleIndex, FldPathX);
                cy += Eval(layer.particlePathY, own, spec.seed, particleIndex, FldPathY);
            }

            // Rotation: spin → yaw about Y (rolls the ellipse in-plane), gemTilt → tilt about X (opens/closes it).
            float yaw = Eval(layer.particleSpin, own, spec.seed, particleIndex, FldSpin) * Mathf.Deg2Rad;
            float tilt = Eval(layer.gemTilt, own, spec.seed, particleIndex, FldGemTilt) * Mathf.Deg2Rad;
            float cyw = Mathf.Cos(yaw), syw = Mathf.Sin(yaw);
            float ct = Mathf.Cos(tilt), st = Mathf.Sin(tilt);
            // Edge-on degeneracy: |cos tilt| ≈ 0 (the design's check) collapses the ellipse to an invisible sliver;
            // |cos yaw| ≈ 0 does the same on the other axis AND would divide by zero in the inverse map below.
            if (Mathf.Abs(ct) < 0.02f || Mathf.Abs(cyw) < 0.02f) return;

            // Forward map, local (u,v,0) → screen:  lx = u·cyw ;  ly = u·(syw·st) + v·ct.  The screen-space plane
            // normal is Rot(0,0,1) = (syw, −cyw·st, cyw·ct); pick the viewer-facing side (N.z > 0) — the ring is
            // two-sided so we never cull it, we just shade whichever face points at the camera. N is CONSTANT (the
            // plane is flat), so it's computed once here, not per pixel.
            float sywst = syw * st;
            Vector3 N = new Vector3(syw, -cyw * st, cyw * ct);
            if (N.z < 0f) N = -N;

            float lyaw = layer.gemLightYaw * Mathf.Deg2Rad, lpitch = layer.gemLightPitch * Mathf.Deg2Rad;
            float ldist = layer.gemLightDistance * R, lhoriz = ldist * Mathf.Cos(lpitch);
            Vector3 lightPos = new Vector3(lhoriz * Mathf.Sin(lyaw), ldist * Mathf.Sin(lpitch), lhoriz * Mathf.Cos(lyaw));
            float lrange = (layer.gemLightDistance + 1.2f) * R, lrange2 = lrange * lrange;
            Vector3 viewDir = new Vector3(0f, 0f, 1f);

            var specFill = layer.gemSpecularFill; bool specSpatial = IsSpatialFill(specFill);
            var innerFill = layer.gemInnerGlowFill; bool innerSpatial = IsSpatialFill(innerFill);
            var lineFill = layer.gemLineFill; bool lineSpatial = IsSpatialFill(lineFill);
            var edgeFill = layer.gemEdgeGlowFill; bool edgeSpatial = IsSpatialFill(edgeFill);
            var matFill = layer.shapeFill; bool matSpatial = IsSpatialFill(matFill);
            Color specColor = specFill != null ? specFill.Evaluate(own, 0f, 0f) : Color.white;
            Color innerColor = innerFill != null ? innerFill.Evaluate(own, 0f, 0f) : Color.white;
            Color lineColor = lineFill != null ? lineFill.Evaluate(own, 0f, 0f) : Color.white;
            Color edgeGlowColor = edgeFill != null ? edgeFill.Evaluate(own, 0f, 0f) : Color.white;
            float lineW = layer.gemLineWidth;
            float ambient = layer.gemAmbient, specStr = layer.gemSpecular;
            float diffuse = layer.gemDiffuse, specPow = layer.gemSpecPower;   // P5: the DIFFUSE strength (was hardcoded 2.1) + specular exponent (was hardcoded 48)

            float edgeGlow = Mathf.Clamp01(Eval(layer.gemEdgeGlow, own, spec.seed, particleIndex, FldGemEdgeGlow));
            float innerGlow = Mathf.Clamp01(Eval(layer.gemInnerGlow, own, spec.seed, particleIndex, FldGemInnerGlow));
            float haloR = Mathf.Max(2.5f, 0.24f * R);
            float innerGlowR = Mathf.Max(3f, 0.30f * R);   // inner-GLOW band radius (distinct from innerR, the hole)
            // Hoisted material fill (default OverLife = gradient.Evaluate(own), byte-identical); a spatial material
            // recomputes per pixel at (lx/R, ly/R) below.
            Color baseCol = matFill != null ? matFill.Evaluate(own, 0f, 0f) : Color.white;

            // Screen bounds of the (sheared) outer ellipse + halo:  |lx| ≤ R·|cyw|,  |ly| ≤ R·(|syw·st| + |ct|).
            float exHalf = R * Mathf.Abs(cyw) + haloR;
            float eyHalf = R * (Mathf.Abs(sywst) + Mathf.Abs(ct)) + haloR;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - exHalf));
            int x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + exHalf));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - eyHalf));
            int y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + eyHalf));
            int pHash = Hash(spec.seed, particleIndex, FldModifier, 7 + _layerSalt);
            float invHole = 1f / Mathf.Max(0.001f, R - innerR);   // for crossFrac (0 at inner rim, 1 at outer)

            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float lx = x + 0.5f - cx, ly = y + 0.5f - cy;
                    // Spatial fills at this pixel's particle-local point (lx/R, ly/R) — or canvas-anchored (Fixed),
                    // per fill via EvalFill; constant fills skip (byte-identical).
                    float fu = lx / R, fv = ly / R;
                    if (lineSpatial) lineColor = EvalFill(lineFill, own, fu, fv, x, y, W, H);
                    if (edgeSpatial) edgeGlowColor = EvalFill(edgeFill, own, fu, fv, x, y, W, H);
                    if (innerSpatial) innerColor = EvalFill(innerFill, own, fu, fv, x, y, W, H);
                    if (specSpatial) specColor = EvalFill(specFill, own, fu, fv, x, y, W, H);
                    if (matSpatial) baseCol = EvalFill(matFill, own, fu, fv, x, y, W, H);
                    // Invert the forward map:  u = lx/cyw ;  v = (ly − u·syw·st)/ct.
                    float u = lx / cyw;
                    float v = (ly - u * sywst) / ct;
                    float rho = Mathf.Sqrt(u * u + v * v);

                    // Band SDF in ring-plane (local) units: < 0 inside the annulus, > 0 past either rim.
                    float dBand = Mathf.Max(innerR - rho, rho - R);
                    // Gradient-magnitude correction (the Bakery lesson: trueDist ≈ Δf/|∇f|). The tilt (and spin)
                    // compress the ellipse in screen space, so a fixed ring-plane distance spans FEWER screen
                    // pixels near the ellipse's flat sides than at its ends — an uncorrected local rim distance
                    // would draw a rim line that visibly thins there. Dividing the local distance by |∇ρ| (the
                    // local-to-screen contraction along the rim normal) restores a uniform SCREEN thickness all the
                    // way around. |∇ρ| is derived from the inverse map's partials (∂u/∂lx=1/cyw, ∂v/∂lx=−syw·st/
                    // (cyw·ct), ∂v/∂ly=1/ct); ∂ρ/∂lx = (u/cyw − v·syw·st/(cyw·ct))/ρ, ∂ρ/∂ly = v/(ct·ρ). Floored at
                    // 0.2 like Bakery so a near-zero gradient can't blow the distance up.
                    float drdx = rho > 1e-4f ? (u / cyw - v * sywst / (cyw * ct)) / rho : 0f;
                    float drdy = rho > 1e-4f ? (v / ct) / rho : 0f;
                    float gradMag = Mathf.Max(0.2f, Mathf.Sqrt(drdx * drdx + drdy * drdy));
                    float rimDist = Mathf.Abs(dBand) / gradMag;   // screen-space distance to the NEAREST rim (both)

                    bool hasFace = dBand <= 0f;
                    bool isLine = false;
                    float fr = 0f, fg = 0f, fb = 0f;
                    if (hasFace)
                    {
                        // 3D point on the tilted plane at this pixel (orthographic: its screen x/y ARE lx/ly; z from
                        // the forward map's P.z = v·st − u·syw·ct).
                        float pz = v * st - u * syw * ct;
                        Vector3 P = new Vector3(lx, ly, pz);
                        Vector3 toL = lightPos - P;
                        float dist = toL.magnitude;
                        Vector3 L = toL / Mathf.Max(1e-4f, dist);
                        float atten = 1f / (1f + dist * dist / lrange2);
                        float ndl = Mathf.Max(0f, Vector3.Dot(N, L));
                        float lit = ambient + diffuse * ndl * atten;
                        if (rimDist <= lineW)   // BOTH rims draw the hard line (screen-distance, gradient-corrected)
                        {
                            isLine = true;
                            float k = Mathf.Clamp(0.25f + lit, 0f, 1.15f);
                            fr = lineColor.r * k; fg = lineColor.g * k; fb = lineColor.b * k;
                        }
                        else
                        {
                            Vector3 Hh = (L + viewDir).normalized;
                            float sp = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(N, Hh)), specPow);
                            float sAdd = specStr * sp * atten;
                            fr = baseCol.r * lit + specColor.r * sAdd;
                            fg = baseCol.g * lit + specColor.g * sAdd;
                            fb = baseCol.b * lit + specColor.b * sAdd;
                        }
                    }

                    float halo = Mathf.Pow(Mathf.Max(0f, 1f - rimDist / haloR), 2f);
                    float haloAmt = 0.85f * edgeGlow * halo;

                    if (hasFace)
                    {
                        fr += edgeGlowColor.r * haloAmt; fg += edgeGlowColor.g * haloAmt; fb += edgeGlowColor.b * haloAmt;
                        if (!isLine)
                        {
                            // Inner glow spreads across the band inward from BOTH rims (rimDist is the nearest-rim
                            // screen distance, so it's 0 at either rim and peaks at the band's middle).
                            float core = Mathf.Pow(Mathf.Clamp01(rimDist / innerGlowR), 1.4f);
                            float innerAmt = 0.75f * innerGlow * core;
                            fr += innerColor.r * innerAmt; fg += innerColor.g * innerAmt; fb += innerColor.b * innerAmt;
                        }
                        float rr = Mathf.Clamp01(fr * brightMul), gg = Mathf.Clamp01(fg * brightMul), bb = Mathf.Clamp01(fb * brightMul);
                        float pa = alphaEnv;
                        if (mods.AnyPix)
                        {
                            Color pc = new Color(rr, gg, bb, 1f);
                            float crossFrac = Mathf.Clamp01((rho - innerR) * invHole);
                            if (!ApplyPix(mods.pix, ref pc, ref pa, x, y, x + 0.5f, y + 0.5f, frameIndex, crossFrac, own, pHash, W, H)) continue;
                            Over(buf, y * W + x, pc.r, pc.g, pc.b, pa);
                        }
                        else Over(buf, y * W + x, rr, gg, bb, pa);
                    }
                    else
                    {
                        // Outside-halo fragment — past the outer rim OR inside the hole (both are ring "edges").
                        float pa = Mathf.Clamp01(haloAmt) * alphaEnv;
                        if (pa <= 0.002f) continue;
                        float rr = Mathf.Clamp01(edgeGlowColor.r * brightMul), gg = Mathf.Clamp01(edgeGlowColor.g * brightMul), bb = Mathf.Clamp01(edgeGlowColor.b * brightMul);
                        if (mods.AnyPix)
                        {
                            Color pc = new Color(rr, gg, bb, 1f);
                            float crossFrac = Mathf.Clamp01((rho - innerR) * invHole);
                            if (!ApplyPix(mods.pix, ref pc, ref pa, x, y, x + 0.5f, y + 0.5f, frameIndex, crossFrac, own, pHash, W, H)) continue;
                            Over(buf, y * W + x, pc.r, pc.g, pc.b, pa);
                        }
                        else Over(buf, y * W + x, rr, gg, bb, pa);
                    }
                }
        }

        // ── facet-solid geometry (model verts + triangle faces + LINE-edge set), one Build per form ──────────
        // Convention shared by all four: y up, +z toward the viewer; origin strictly INSIDE the convex solid so
        // DrawFacetSolid's centroid test resolves each face's outward normal. `lineEdges` names the edges that draw
        // as hard lines AND seed the inner glow; null = every edge is a line (Gem only). Edge keys use the same
        // `min*64 + max` packing DrawFacetSolid dedupes with (valid while every vertex index < 64 — true here: Gem
        // ≤ 10, Box 8, Pyramid 5, Can 34).
        static int EdgeKey(int i, int j) => i < j ? i * 64 + j : j * 64 + i;

        // Gem: n girdle verts + a crown apex + a pavilion apex, VERBATIM from the original DrawGem so a Gem stays
        // byte-identical. lineEdges == null ⇒ every visible facet boundary is a line (the gem's original look).
        static void BuildGemGeometry(PyrePlusLayer layer, float R, out Vector3[] model, out int[][] faces, out HashSet<int> lineEdges)
        {
            int n = Mathf.Clamp(layer.gemSides, 3, 8);
            float crownH = R * layer.gemCrown;
            float pavD = R * layer.gemPavilion;

            // Model: n girdle verts (index 0..n-1) at PI/2 + 2πk/n scaled R, crown apex (n) above, pavilion apex
            // (n+1) below. y is up, +z toward the viewer after rotation.
            model = new Vector3[n + 2];
            for (int k = 0; k < n; k++)
            {
                float a = Mathf.PI * 0.5f + 2f * Mathf.PI * k / n;
                model[k] = new Vector3(R * Mathf.Cos(a), 0f, R * Mathf.Sin(a));
            }
            model[n] = new Vector3(0f, crownH, 0f);
            model[n + 1] = new Vector3(0f, -pavD, 0f);

            // Faces (generalize the prototype's `& 3` → `% n`): n crown triangles apexT+girdle(i,i+1), then n
            // pavilion triangles apexB+girdle(i+1,i). Outward winding is fixed per face by the centroid test.
            faces = new int[2 * n][];
            for (int i = 0; i < n; i++) faces[i] = new[] { n, i, (i + 1) % n };
            for (int i = 0; i < n; i++) faces[n + i] = new[] { n + 1, (i + 1) % n, i };
            lineEdges = null;   // every edge is a line edge — the gem's original behaviour
        }

        // Box: a real cuboid — half-extents (R, R·solidAspect, R·solidDepth). 8 shared verts, 6 quads → 12 tris.
        // The genuinely-shared vertices are what make the silhouette CLOSE (the Bakery oblique-box bug was two
        // faces extruded in different directions that never met — impossible here since all faces index the same
        // 8 corners). Each quad's splitting diagonal is NON-line, so the line set is exactly the 12 cube edges.
        static void BuildBoxGeometry(PyrePlusLayer layer, float R, out Vector3[] model, out int[][] faces, out HashSet<int> lineEdges)
        {
            float hx = R, hy = R * layer.solidAspect, hz = R * layer.solidDepth;
            model = new[]
            {
                new Vector3(-hx, -hy, -hz), // 0
                new Vector3( hx, -hy, -hz), // 1
                new Vector3( hx,  hy, -hz), // 2
                new Vector3(-hx,  hy, -hz), // 3
                new Vector3(-hx, -hy,  hz), // 4
                new Vector3( hx, -hy,  hz), // 5
                new Vector3( hx,  hy,  hz), // 6
                new Vector3(-hx,  hy,  hz), // 7
            };
            // Each quad is a coplanar face loop (v0,v1,v2,v3); winding is fixed by DrawFacetSolid's centroid test.
            int[][] quads =
            {
                new[] { 0, 1, 2, 3 }, // -Z back
                new[] { 4, 5, 6, 7 }, // +Z front
                new[] { 0, 1, 5, 4 }, // -Y bottom
                new[] { 3, 2, 6, 7 }, // +Y top
                new[] { 0, 3, 7, 4 }, // -X left
                new[] { 1, 2, 6, 5 }, // +X right
            };
            faces = new int[12][];
            lineEdges = new HashSet<int>();
            for (int q = 0; q < quads.Length; q++)
            {
                int a = quads[q][0], b = quads[q][1], c = quads[q][2], d = quads[q][3];
                faces[q * 2]     = new[] { a, b, c };
                faces[q * 2 + 1] = new[] { a, c, d };
                // the four perimeter edges are cube edges (lines); the (a,c) diagonal is the seam (non-line).
                lineEdges.Add(EdgeKey(a, b));
                lineEdges.Add(EdgeKey(b, c));
                lineEdges.Add(EdgeKey(c, d));
                lineEdges.Add(EdgeKey(d, a));
            }
        }

        // Pyramid: apex at (0, +R·solidAspect, 0), a rectangular base of half-width R (x) × R·solidDepth (z) at
        // y = -R·solidAspect·0.35. 4 side tris + a base quad split into 2 tris (its diagonal non-line). At the
        // default solidDepth == 1 the base is the square half-width-R base the design specifies; a non-default
        // Depth stretches it front-to-back so the shown Depth control is live. Line set = 4 apex spokes + 4 base
        // perimeter edges.
        static void BuildPyramidGeometry(PyrePlusLayer layer, float R, out Vector3[] model, out int[][] faces, out HashSet<int> lineEdges)
        {
            float apexY = R * layer.solidAspect;
            float baseY = -R * layer.solidAspect * 0.35f;
            float hz = R * layer.solidDepth;
            model = new[]
            {
                new Vector3(0f, apexY, 0f), // 0 apex
                new Vector3(-R, baseY, -hz), // 1
                new Vector3( R, baseY, -hz), // 2
                new Vector3( R, baseY,  hz), // 3
                new Vector3(-R, baseY,  hz), // 4
            };
            faces = new[]
            {
                new[] { 0, 1, 2 }, // side
                new[] { 0, 2, 3 }, // side
                new[] { 0, 3, 4 }, // side
                new[] { 0, 4, 1 }, // side
                new[] { 1, 2, 3 }, // base tri 1
                new[] { 1, 3, 4 }, // base tri 2 — diagonal (1,3) is the seam (non-line)
            };
            lineEdges = new HashSet<int>
            {
                EdgeKey(0, 1), EdgeKey(0, 2), EdgeKey(0, 3), EdgeKey(0, 4), // apex spokes
                EdgeKey(1, 2), EdgeKey(2, 3), EdgeKey(3, 4), EdgeKey(4, 1), // base perimeter
            };
        }

        // Can: a vertical cylinder approximated as a 16-sided prism — radius R, half-height R·solidAspect
        // (solidDepth is unused; the cross-section is circular). 16 top-rim + 16 bottom-rim verts + 2 cap centres.
        // Barrel = 16 quads → 32 tris; each cap = a 16-tri fan. LINE edges = the two cap rims ONLY; the barrel's
        // vertical seams + quad diagonals + cap fan spokes are all non-line. Flat per-face barrel normals band into
        // 16 strips — accepted retro banding for this slice.
        static void BuildCanGeometry(PyrePlusLayer layer, float R, out Vector3[] model, out int[][] faces, out HashSet<int> lineEdges)
        {
            const int seg = 16;
            float hy = R * layer.solidAspect;
            model = new Vector3[seg * 2 + 2];
            for (int k = 0; k < seg; k++)
            {
                float a = 2f * Mathf.PI * k / seg;
                float vx = R * Mathf.Cos(a), vz = R * Mathf.Sin(a);
                model[k] = new Vector3(vx, hy, vz);          // top rim 0..15
                model[seg + k] = new Vector3(vx, -hy, vz);   // bottom rim 16..31
            }
            int topC = seg * 2;       // 32
            int botC = seg * 2 + 1;   // 33
            model[topC] = new Vector3(0f, hy, 0f);
            model[botC] = new Vector3(0f, -hy, 0f);

            var faceList = new List<int[]>(seg * 4);
            lineEdges = new HashSet<int>();
            for (int k = 0; k < seg; k++)
            {
                int k1 = (k + 1) % seg;
                int t0 = k, t1 = k1, b0 = seg + k, b1 = seg + k1;
                faceList.Add(new[] { t0, t1, b1 });   // barrel quad, tri 1
                faceList.Add(new[] { t0, b1, b0 });   // barrel quad, tri 2 (diagonal + verticals non-line)
                faceList.Add(new[] { topC, t0, t1 }); // top cap fan
                faceList.Add(new[] { botC, b1, b0 }); // bottom cap fan
                lineEdges.Add(EdgeKey(t0, t1));       // top rim (line)
                lineEdges.Add(EdgeKey(b0, b1));       // bottom rim (line)
            }
            faces = faceList.ToArray();
        }

        // Barycentric point-in-triangle (orthographic: screen bary == plane bary). Weights out. Ported verbatim
        // from the approved gem prototype.
        static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out float w0, out float w1, out float w2)
        {
            w0 = w1 = w2 = 0f;
            float d = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
            if (Mathf.Abs(d) < 1e-6f) return false;
            w0 = ((b.y - c.y) * (p.x - c.x) + (c.x - b.x) * (p.y - c.y)) / d;
            w1 = ((c.y - a.y) * (p.x - c.x) + (a.x - c.x) * (p.y - c.y)) / d;
            w2 = 1f - w0 - w1;
            return w0 >= 0f && w1 >= 0f && w2 >= 0f;
        }

        // Distance from a point to a segment (screen space). Ported verbatim from the approved gem prototype.
        static float DistSeg(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return (p - (a + ab * t)).magnitude;
        }

        static void Over(Color32[] buf, int i, float r, float g, float b, float a)
        {
            var dst = buf[i];
            float da = dst.a * (1f / 255f);
            float outA = a + da * (1f - a);
            if (outA <= 0.0001f) { buf[i] = Transparent; return; }
            float inv = 1f / outA;
            buf[i] = new Color32(
                (byte)(Mathf.Clamp01((r * a + dst.r * (1f / 255f) * da * (1f - a)) * inv) * 255f),
                (byte)(Mathf.Clamp01((g * a + dst.g * (1f / 255f) * da * (1f - a)) * inv) * 255f),
                (byte)(Mathf.Clamp01((b * a + dst.b * (1f / 255f) * da * (1f - a)) * inv) * 255f),
                (byte)(Mathf.Clamp01(outA) * 255f));
        }

        /// True when a fill VARIES with the pixel's local (u,v) — the spatial gradient modes (Linear / Radial) OR
        /// any TEXTURE (sprite / noise / grid / dots all sample per (u,v)). The Solid and OverLife modes ignore
        /// (u,v), so a fill in either can be evaluated ONCE and hoisted; only a spatial / textured fill needs
        /// per-pixel re-evaluation. Every default PyrePlus fill is Solid or OverLife with texture None, so this is
        /// false for defaults and the per-pixel recompute paths below stay skipped — keeping defaults byte-identical.
        static bool IsSpatialFill(ZuiFill f) =>
            f != null && (f.texture != ZuiFill.TextureKind.None
                          || f.mode == ZuiFill.Mode.Linear || f.mode == ZuiFill.Mode.Radial);

        /// Evaluate a SPATIAL fill/texture at a pixel, honouring its coordinate SPACE (P5-B). Stamped (the default)
        /// feeds the caller's shape-LOCAL (lu,lv) — the pattern rotates / spins / travels WITH the shape — so this
        /// branch calls the exact same fill.Evaluate(life, lu, lv) the pre-P5 code did (byte-identical for every
        /// default fill, which is Stamped). Fixed feeds CANVAS-anchored coords instead: the pixel's absolute -1..1
        /// position on the canvas (unrotated, unspun, untravelled), so the shape moves THROUGH a stationary pattern
        /// (mask-like). Matches the background fill's own canvas (u,v): (px+0.5 − W/2)/(W/2) ≡ (px+0.5)/W·2 − 1.
        /// `f` is always non-null here — every call site is gated by IsSpatialFill (or an explicit != null).
        static Color EvalFill(ZuiFill f, float life, float lu, float lv, int px, int py, int W, int H)
        {
            if (f.space == ZuiFill.FillSpace.Fixed)
                return f.Evaluate(life, (px + 0.5f - W * 0.5f) / (W * 0.5f), (py + 0.5f - H * 0.5f) / (H * 0.5f));
            return f.Evaluate(life, lu, lv);
        }

        /// True when a value is exactly Static 0 — the default state of the T7 opt-in travel-path fields. Gates
        /// their Evals so a default asset's particle centre (and thus its whole raster) stays byte-identical; a
        /// Curve or MinMax value always evaluates. A null value counts as zero (Eval(null) == 0 too).
        static bool IsStaticZero(ZUIValue v) => v == null || (v.mode == ZUIValue.Mode.Static && v.staticValue == 0f);

        /// True when a value is EXACTLY Static 1 — the default state of swarmScaleByIndex (S1). Gates the per-
        /// particle size multiply so a default swarm skips the Eval entirely and its sizeMul stays byte-identical;
        /// a Curve/MinMax (or any non-1 Static) always evaluates. A null value counts as one (a missing field must
        /// be a ×1 no-op, NOT Eval(null)'s 0 which would collapse every particle).
        static bool IsStaticOne(ZUIValue v) => v == null || (v.mode == ZUIValue.Mode.Static && v.staticValue == 1f);

        /// True when swarmSpawnTiming is EXACTLY a two-point Curve from (0,0) to (1,K) — out `k` = that endpoint
        /// value. This is the fast-path gate for the Window timing: when true the caller skips Eval and uses the
        /// verbatim `K * i / (n-1)` arithmetic (the pre-removal `window * i / (n-1)` with K in window's place), so a
        /// default/linear-shaped timing stays byte-identical rather than round-tripping through Eval →
        /// EnvelopeEvaluator. The endpoint's segment exponent is intentionally NOT checked — the fast path returns
        /// the scaled even fraction directly, treating a bent 2-point curve as the line it visually approximates
        /// (the default factory's exponent is 1 anyway). Any non-two-point / non-(0,0)-start / MinMax / Static
        /// value returns false (k = 0) and goes through Eval, whose result is the whole spawn-life mapping.
        static bool IsLinearTo(ZUIValue v, out float k)
        {
            k = 0f;
            if (v == null || v.mode != ZUIValue.Mode.Curve || v.points == null || v.points.Count != 2) return false;
            var p0 = v.points[0];
            var p1 = v.points[1];
            if (p0.time == 0f && p0.value == 0f && p1.time == 1f) { k = p1.value; return true; }
            return false;
        }

        /// Evaluate a ZUIValue for a given particle deterministically — Static reads the value, Curve reads
        /// the envelope at the particle's life, MinMax draws once from a seeded RNG keyed by (seed, index,
        /// field). Mirrors BlastRenderer.Eval; MinMax is what makes each particle differ reproducibly.
        static float Eval(ZUIValue v, float life, int seed, int particleIndex, int fieldId)
        {
            if (v == null) return 0f;
            switch (v.mode)
            {
                case ZUIValue.Mode.Static: return v.staticValue;
                case ZUIValue.Mode.MinMax:
                {
                    var rng = new System.Random(Hash(seed, particleIndex, fieldId, _layerSalt));
                    return Mathf.Lerp(v.min, v.max, (float)rng.NextDouble());
                }
                case ZUIValue.Mode.Curve:
                    // Points are authored in normalized [0..1]; sample directly (ignore duration/warmup/
                    // cooldown — that's the runtime-seconds API, not our frame-baked timeline). EvaluateRaw
                    // would divide life by duration (default 4s), sweeping only the curve's first quarter.
                    return ZUIEnvelopeEvaluator.Evaluate(v.points, Mathf.Clamp01(life), v.yMax);
                case ZUIValue.Mode.Steps:
                {
                    // The step-sequencer mode, mapped onto the frame-baked timeline exactly like Curve: life
                    // divides into steps.Count equal HELD sections (timing fields ignored, same reasoning as
                    // Curve above). Previously fell to `default` and silently rendered as the static value —
                    // an authored Steps pattern drew flat with no warning.
                    var steps = v.steps;
                    int n = steps != null ? steps.Count : 0;
                    if (n == 0) return v.staticValue;
                    return steps[Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(life) * n), 0, n - 1)];
                }
                default: return v.staticValue;
            }
        }

        /// The renderer's one seeded hash — public so a PlusForm derives every random draw from the same function
        /// (and a migrated form can reproduce the exact seed stream of the enum case it replaced).
        public static int Hash(int a, int b, int c, int d)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)a) * 16777619u;
                h = (h ^ (uint)b) * 16777619u;
                h = (h ^ (uint)c) * 16777619u;
                h = (h ^ (uint)d) * 16777619u;
                return (int)h;
            }
        }

        /// A WELL-AVALANCHED uniform in [0,1) from the four integer keys — distinct from the weaker `Hash01` above,
        /// which takes the low 24 bits of the raw FNV Hash with NO finalizer (fine for the per-pixel Sparkle draws it
        /// serves, kept byte-identical). The Area placement samplers (SampleDisc / SamplePolygonArea) need STRONGER
        /// decorrelation: the pre-fix code built one fresh System.Random keyed by the raw Hash and took its first two
        /// or three NextDouble()s, but the raw Hash barely changes between neighbouring particleIndex values and
        /// System.Random's first NextDouble() is a near-linear function of its seed, so consecutive particles drew
        /// near-identical first uniforms → the disc/polygon "spokes" artifact. Running the raw key through Murmur3's
        /// fmix32 finalizer fully avalanches it (one input-bit flip ⇒ ~half the output bits flip), so neighbouring
        /// indices decorrelate and Area placement covers the shape uniformly by area. Deterministic + seeded (same
        /// keys ⇒ same value). NOTE: deliberately does NOT change the global Hash or the weak Hash01, so every other
        /// caller (Eval MinMax, per-particle/pixel hashes, Sparkle, Gem, …) is byte-identical.
        static float HashUniform(int a, int b, int c, int d)
        {
            unchecked
            {
                uint h = (uint)Hash(a, b, c, d);
                h ^= h >> 16; h *= 0x85ebca6bu;
                h ^= h >> 13; h *= 0xc2b2ae35u;
                h ^= h >> 16;
                return (float)(h * (1.0 / 4294967296.0));   // [0,1)  (2^32 divisor)
            }
        }

        public static Texture2D RenderFrameTexture(PyrePlusSpec spec, int frameIndex)
        {
            int W = spec != null ? spec.Width : 1;
            int H = spec != null ? spec.Height : 1;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixels32(RenderFrame(spec, frameIndex));
            tex.Apply();
            return tex;
        }
    }
}
