// PyreToPlusConverter — Slice 9: import a vanilla Pyre (née BlastSpec) asset into a new PyrePlusSpec.
//
// The capstone of the PyrePlus advanced-forms work: because slices 0–8 brought every Pyre1 form + the full
// luma-matte model + the per-layer simulation slot into PyrePlus, a Pyre1 asset is now genuinely convertible.
// The per-form field maps in PYREPLUS_ADVANCED_DESIGN.md ARE this converter's dispatch table.
//
// PURE + additive: ConvertFromPyre never mutates or saves the SOURCE (it Clones the one field — HeightBalls
// groups — whose accessor would otherwise upgrade the source in place), never touches AssetDatabase, and never
// forks the runtime renderer. It returns a fresh in-memory PyrePlusSpec (the window button is what CreateAssets
// it). Everything it drops or approximates is reported through the `warnings` list, per layer.
//
// KNOWN structural mismatches, surfaced as warnings rather than silently guessed:
//   • PyrePlus canvases are SQUARE and centre-pivoted → non-square canvasHeight and a non-centre `origin` warn.
//   • PyrePlus has no per-layer [start,end] frame window; a layer's timing is reproduced through the swarm's
//     spawn-timing + particle-life when the layer scatters, and warned when a single (count 1) shape can't.
//   • MetaBlob's hand-placed orbs / HeightBalls' authored ball groups have no procedural-swarm equal, so they
//     convert to a Fuse / Ramp swarm that reproduces the LOOK, not the exact placements (warned).
using System.Collections.Generic;
using Laubrary.Pyre;
using Laubrary.SpriteFx;
using UnityEngine;
// `Pyre` (the class) can't be named bare from here — the enclosing `Laubrary` namespace exposes the `Laubrary.Pyre`
// NAMESPACE under that same name, which wins, so a bare `Pyre` reads as a namespace. Alias the class explicitly.
using PyreAsset = Laubrary.Pyre.Pyre;
// NOTE on the two enums that exist in BOTH namespaces (MatteChannel, MatteScope): a bare reference here resolves
// to the PyrePlus one, because a type in an ENCLOSING namespace (Laubrary.PyrePlus) wins over one pulled in by a
// `using` directive (Laubrary.Pyre). Pyre's copies are only ever reached via `src.` field access + an int cast.

namespace Laubrary.PyrePlus.Editor
{
    public static class PyreToPlusConverter
    {
        /// Convert a vanilla Pyre asset into a NEW in-memory PyrePlusSpec, collecting every dropped/approximated
        /// aspect into <paramref name="warnings"/>. Pure: the source is never mutated (HeightBalls group access is
        /// done on a Clone) or saved; the returned spec is created with ScriptableObject.CreateInstance and owned
        /// by the caller (the window button CreateAssets it; a probe inspects it in memory then destroys it).
        public static PyrePlusSpec ConvertFromPyre(PyreAsset src, out List<string> warnings)
        {
            warnings = new List<string>();
            var dst = ScriptableObject.CreateInstance<PyrePlusSpec>();
            if (src == null) { warnings.Add("Source Pyre asset was null — produced an empty default spec."); return dst; }

            // ── SPEC-level (direct) ──────────────────────────────────────────────────
            dst.seed = src.seed;
            dst.frameCount = Mathf.Max(1, src.frameCount);
            dst.canvasSize = Mathf.Clamp(Mathf.Max(1, src.canvasSize), 16, 256);
            dst.pixelsPerUnit = src.pixelsPerUnit;
            dst.background = src.background;
            dst.backgroundUseFill = false;   // a plain Pyre background is the flat clear

            int srcH = src.canvasHeight > 0 ? src.canvasHeight : src.canvasSize;
            if (srcH != src.canvasSize)
                warnings.Add($"Non-square canvas {src.canvasSize}x{srcH}: PyrePlus canvases are square — used {dst.canvasSize}x{dst.canvasSize} (height dropped).");
            if ((src.origin - new Vector2(0.5f, 0.5f)).sqrMagnitude > 1e-6f)
                warnings.Add($"Sprite origin/pivot {src.origin} has no PyrePlus equivalent (always centred) — dropped.");

            // Global (blast-wide) geometry/pixel/post modifiers are duplicated into EVERY converted layer's stack
            // (PyrePlus has no blast-wide modifier slot). Cloned per layer below so they never share instances.
            var globalMods = src.globalModifiers != null && src.globalModifiers.Count > 0 ? src.globalModifiers : null;
            if (globalMods != null)
                warnings.Add($"{globalMods.Count} global modifier(s) duplicated into each layer's own modifier stack (PyrePlus has no blast-wide slot).");
            if (src.simulationModifier != null)
                warnings.Add("Blast-wide simulation modifier dropped: PyrePlus has only a PER-LAYER simulation slot, not a blast-wide one.");

            dst.layers = new List<PyrePlusLayer>();
            if (src.layers != null)
            {
                for (int i = 0; i < src.layers.Count; i++)
                {
                    var l = src.layers[i];
                    if (l == null) continue;
                    ConvertLayer(l, src, globalMods, dst.layers, warnings);
                }
            }
            // A PyrePlus spec must hold at least one layer.
            if (dst.layers.Count == 0) { dst.layers.Add(new PyrePlusLayer()); warnings.Add("Source had no usable layers — emitted one empty default layer."); }
            return dst;
        }

        // ── per-layer dispatch ───────────────────────────────────────────────────────
        static void ConvertLayer(Layer src, PyreAsset spec, List<PyreModifier> globalMods,
                                 List<PyrePlusLayer> outList, List<string> warnings)
        {
            string label = string.IsNullOrEmpty(src.name) ? "(layer)" : src.name;

            // Bars is its own decomposition (Streak + Line/Ring swarm, optional mirror layer) and ignores scatter.
            if (src.shape == LayerShape.Bars) { ConvertBars(src, spec, globalMods, warnings, outList); return; }

            // Rosing scatter → ONE PyrePlus layer per RoseRing (preserve the bloom). Only meaningful for the
            // compositing forms; MetaBlob/HeightBalls/Fire/Fireball place themselves, so Rosing is ignored there.
            bool scatterForm = src.shape == LayerShape.Disc || src.shape == LayerShape.Crescent
                            || src.shape == LayerShape.SparkleField || src.shape == LayerShape.Sprite;
            if (src.scatterMode == ScatterMode.Rosing && scatterForm && src.roseRings != null && src.roseRings.Count > 0)
            {
                for (int r = 0; r < src.roseRings.Count; r++)
                {
                    var ring = src.roseRings[r];
                    if (ring == null) continue;
                    var dst = BuildBaseLayer(src, spec, globalMods, warnings, label + $" · rose {r + 1}");
                    ApplyForm(src, dst, spec, warnings, label);
                    SetupRoseRingSwarm(src, dst, spec, ring);
                    outList.Add(dst);
                }
                warnings.Add($"{label}: Rosing scatter expanded to {src.roseRings.Count} layer(s), one per ring (bloom preserved).");
                return;
            }

            var one = BuildBaseLayer(src, spec, globalMods, warnings, label);
            ApplyForm(src, one, spec, warnings, label);
            outList.Add(one);
        }

        // Shared fields every converted layer carries, regardless of form: identity, alpha/size/spin envelopes,
        // colour → shapeFill, the modifier stack (+ globals), and the matte role. Form + swarm are added on top.
        static PyrePlusLayer BuildBaseLayer(Layer src, PyreAsset spec, List<PyreModifier> globalMods,
                                            List<string> warnings, string label)
        {
            var dst = new PyrePlusLayer
            {
                name = src.name ?? "Layer",
                enabled = src.enabled,
                alpha = CloneVal(src.alpha),
                size = CloneVal(src.size),
                particleSpin = CloneVal(src.spinDegrees),
                edgeSoftness = Mathf.Clamp01(Peak(src.outerSoftness)),
            };

            // Colour: ColorMode + colorOverLife → a ZuiFill.
            dst.shapeFill = MakeFill(src, warnings, label);

            // Modifiers: the layer's own, then the duplicated globals.
            CopyModifiers(src.modifiers, dst, warnings, label);
            CopyModifiers(globalMods, dst, warnings, label);
            if (src.simulationModifier != null)
            {
                if (dst.simulationModifier == null) dst.simulationModifier = (SimulationModifier)src.simulationModifier.Clone();
                else warnings.Add($"{label}: a second simulation modifier was dropped (one per-layer slot).");
            }

            // Matte: Pyre1's luma matte ports DIRECTLY to PyrePlus's LumaMatte role (slice 4a).
            if (src.role == LayerRole.Matte)
            {
                dst.matteEnabled = true;
                dst.matteRole = MatteRole.LumaMatte;
                dst.matteFlags = (MatteChannel)(int)src.matteChannel;        // same [Flags] bit values
                dst.matteScope = (MatteScope)(int)src.matteScope;
                dst.matteInvert = src.matteInvert;
                dst.matteStrength = CloneVal(src.matteStrength);
                dst.matteBlurAmount = CloneVal(src.matteAmount);             // Pyre `matteAmount` == the Blur radius field
                dst.matteDisplaceAmount = CloneVal(src.matteDisplaceAmount);
                dst.matteHueDegrees = CloneVal(src.matteHueDegrees);
            }
            return dst;
        }

        // ── form dispatch (the field maps) ────────────────────────────────────────────
        static void ApplyForm(Layer src, PyrePlusLayer dst, PyreAsset spec, List<string> warnings, string label)
        {
            switch (src.shape)
            {
                case LayerShape.Disc:
                    dst.shapeForm = ShapeForm.Disc;
                    if (src.hollow)
                        warnings.Add($"{label}: Disc hollow/hole dropped (PyrePlus Disc has no hole) — rendered as a solid disc.");
                    SetupScatterSwarm(src, dst, spec, warnings, label);
                    break;

                case LayerShape.Crescent:
                    dst.shapeForm = ShapeForm.Crescent;
                    MapCrescent(src, dst);
                    SetupScatterSwarm(src, dst, spec, warnings, label);
                    break;

                case LayerShape.SparkleField:
                    dst.shapeForm = ShapeForm.Sparkle;
                    dst.sparkleDensity = CloneVal(src.sparkleDensity);
                    dst.sparkleSize = 1;
                    SetupScatterSwarm(src, dst, spec, warnings, label);
                    break;

                case LayerShape.Sprite:
                    dst.shapeForm = ShapeForm.Sprite;
                    dst.spriteImage = src.particleSprite;
                    dst.spriteTint = true;
                    dst.particleSpin = CloneVal(src.spriteSpin);   // Sprite reuses the shared spin
                    SetupScatterSwarm(src, dst, spec, warnings, label);
                    break;

                case LayerShape.MetaBlob:
                    dst.shapeForm = ShapeForm.Disc;
                    dst.coalesce = LayerCoalesce.Fuse;
                    MapMetaBlob(src, dst, spec, warnings, label);
                    break;

                case LayerShape.HeightBalls:
                    dst.shapeForm = ShapeForm.Disc;
                    dst.coalesce = LayerCoalesce.Ramp;
                    MapHeightBalls(src, dst, spec, warnings, label);
                    break;

                case LayerShape.Fire:
                    dst.shapeForm = ShapeForm.Fire;
                    MapFire(src, dst);
                    break;

                case LayerShape.Fireball:
                    dst.shapeForm = ShapeForm.Fireball;
                    MapFireball(src, dst);
                    break;

                default:
                    dst.shapeForm = ShapeForm.Disc;
                    warnings.Add($"{label}: unhandled Pyre shape {src.shape} — fell back to a plain Disc.");
                    SetupScatterSwarm(src, dst, spec, warnings, label);
                    break;
            }
        }

        static void MapCrescent(Layer src, PyrePlusLayer dst)
        {
            // Pyre's crescent bite disc is an X/Y offset in RADIUS units; PyrePlus authors it as an angle + a
            // 0..1 push-out fraction + a bite-disc size. Convert the vector to polar; the bite disc shares the
            // main radius in Pyre (a ~full-size bite), so keep PyrePlus's default bite.
            float ox = Peak(src.crescentOffsetX), oy = Peak(src.crescentOffsetY);
            float mag = Mathf.Sqrt(ox * ox + oy * oy);
            dst.crescentOffset = Mathf.Clamp01(mag);
            dst.crescentAngle = new ZUIValue(mag > 1e-4f ? Mathf.Atan2(oy, ox) * Mathf.Rad2Deg : 0f);
            dst.crescentBite = new ZUIValue(0.9f);
        }

        static void MapMetaBlob(Layer src, PyrePlusLayer dst, PyreAsset spec, List<string> warnings, string label)
        {
            var orbs = src.metaOrbs;
            int n = orbs != null ? orbs.Count : 0;
            float extent = 4f, avgR = 8f;
            if (n > 0)
            {
                float sumR = 0f;
                foreach (var o in orbs)
                {
                    if (o == null) continue;
                    float r = o.RadiusAt(0f);
                    sumR += r;
                    extent = Mathf.Max(extent, o.pos.magnitude + r);
                }
                avgR = sumR / Mathf.Max(1, n);
            }
            dst.swarmEnabled = true;
            dst.swarmCount = Mathf.Clamp(Mathf.Max(2, n), 2, 128);
            dst.swarmSpawnMode = SwarmSpawnMode.Area;
            dst.swarmShapeKind = SwarmShapeKind.Circle;
            dst.shapeScale = new ZUIValue(Mathf.Max(4f, extent));
            dst.size = new ZUIValue(Mathf.Max(2f, avgR));       // MetaOrb.Radius → layer.size (per-orb life becomes uniform)
            dst.swarmScale = CloneVal(src.metaExpand);          // metaExpand → live swarmScale
            dst.fuseThreshold = src.metaThreshold;
            dst.fuseShadeRange = src.metaShadeRange;
            dst.fuseSoftness = src.metaSoftness;
            SetupWindowTiming(src, dst, spec);
            warnings.Add($"{label}: MetaBlob → Fuse swarm. {n} hand-placed orb(s) approximated by a procedural disc swarm (exact positions + per-orb birth/life not preserved); metaRadiusScale dropped.");
        }

        static void MapHeightBalls(Layer src, PyrePlusLayer dst, PyreAsset spec, List<string> warnings, string label)
        {
            // Access the ball groups on a CLONE so the source layer is never upgraded/mutated in place.
            var groups = src.Clone().HeightBallGroups;
            HeightBallGroup g = null;
            if (groups != null)
                foreach (var gg in groups) { if (gg != null && gg.enabled) { g = gg; break; } }
            if (g == null && groups != null && groups.Count > 0) g = groups[0];

            float half = Mathf.Max(1, spec.canvasSize) * 0.5f;
            dst.swarmEnabled = true;
            dst.swarmSpawnMode = SwarmSpawnMode.Area;
            dst.swarmShapeKind = SwarmShapeKind.Circle;
            if (g != null)
            {
                dst.swarmCount = Mathf.Clamp(Mathf.Max(2, g.waveBalls * Mathf.Max(1, g.waves)), 2, 64);
                dst.shapeScale = ScaleVal(g.cloudSize, half);      // cloudSize is 0..1 of the half-size
                dst.size = CloneVal(g.ballSize);
                dst.density = CloneVal(g.mass);                    // mass → density env
                dst.heat = CloneVal(g.height);                     // height → heat env
                dst.rampRimScale = Mathf.Clamp01(g.surfaceNoise);
            }
            else
            {
                dst.swarmCount = Mathf.Max(2, Mathf.RoundToInt(Peak(src.count)));
                dst.shapeScale = ScaleVal(src.spawnRadius, half);
            }
            dst.rampFusion = Mathf.Clamp01(src.hbFusion);
            dst.rampCoverage = src.hbCoverage;
            dst.rampLighting = src.hbLighting;
            dst.rampRelief = src.hbRelief;
            dst.rampLightAngle = Peak(src.hbLightAngle);
            SetupWindowTiming(src, dst, spec);
            int gc = groups != null ? groups.Count : 0;
            warnings.Add($"{label}: HeightBalls → Ramp swarm from {(gc > 1 ? $"the first of {gc} groups (others" : "its group (waves/churn/squash/fold")} not preserved).");
        }

        static void MapFire(Layer src, PyrePlusLayer dst)
        {
            dst.fireIntensity = CloneVal(src.fireIntensity);
            dst.fireArms = Mathf.Max(1, src.fireArms);
            dst.fireArmMode = src.fireArmMode;
            dst.fireDirection = CloneVal(src.fireDirection);
            dst.fireEmitterWidth = CloneVal(src.fireEmitterWidth);
            dst.fireEmitterInset = CloneVal(src.fireEmitterInset);
            dst.fireHeat = CloneVal(src.fireHeat);
            dst.fireFuel = CloneVal(src.fireFuel);
            dst.firePulse = CloneVal(src.firePulse);
            dst.fireFlow = CloneVal(src.fireFlow);
            dst.fireBuoyancy = CloneVal(src.fireBuoyancy);
            dst.fireCurl = CloneVal(src.fireCurl);
            dst.fireCurlScale = CloneVal(src.fireCurlScale);
            dst.fireFlicker = CloneVal(src.fireFlicker);
            dst.fireStretch = CloneVal(src.fireStretch);
            dst.firePinch = CloneVal(src.firePinch);
            dst.fireBreakup = CloneVal(src.fireBreakup);
            dst.fireDissipation = CloneVal(src.fireDissipation);
            dst.fireBurn = CloneVal(src.fireBurn);
            dst.fireReach = CloneVal(src.fireReach);
            dst.fireEdgeCooling = CloneVal(src.fireEdgeCooling);
            dst.fireSteps = Mathf.Max(1, src.fireSteps);
            dst.fireThreshold = src.fireThreshold;
            dst.fireContrast = src.fireContrast;
        }

        static void MapFireball(Layer src, PyrePlusLayer dst)
        {
            dst.fireballSource = CloneVal(src.fireballSource);
            dst.fireballSourceRadius = CloneVal(src.fireballSourceRadius);
            dst.fireballCooling = CloneVal(src.fireballCooling);
            dst.fireballSharpness = CloneVal(src.fireballSharpness);
            dst.fireballSpread = CloneVal(src.fireballSpread);
            dst.fireballReach = CloneVal(src.fireballReach);
            dst.fireballArms = Mathf.Max(1, src.fireballArms);
            dst.fireballMirror = src.fireballMirror;
            dst.fireballThreshold = src.fireballThreshold;
            dst.fireballContrast = src.fireballContrast;
        }

        // ── Bars → Streak + swarm (Capability 4) ───────────────────────────────────────
        static void ConvertBars(Layer src, PyreAsset spec, List<PyreModifier> globalMods,
                                List<string> warnings, List<PyrePlusLayer> outList)
        {
            string label = string.IsNullOrEmpty(src.name) ? "(bars)" : src.name;
            float half = Mathf.Max(1, spec.canvasSize) * 0.5f;

            if (src.star)
            {
                // Star: the whole bar ROW is duplicated into `spreadCount` arms radiating from centre. PyrePlus can't
                // nest a swarm-of-rows, so this becomes a RING of single outward Streaks (a starburst of comet tails).
                var dst = BuildBaseLayer(src, spec, globalMods, warnings, label);
                dst.shapeForm = ShapeForm.Streak;
                MapStreakBody(src, dst);
                dst.swarmEnabled = true;
                dst.swarmShapeKind = SwarmShapeKind.Circle;
                dst.swarmSpawnMode = SwarmSpawnMode.Path;
                dst.swarmOrient = SwarmOrient.Outward;
                dst.swarmCount = Mathf.Clamp(Mathf.Max(2, src.spreadCount), 2, 128);
                dst.swarmEvenPath = true;
                dst.swarmPathSpread = Mathf.Clamp01(Peak(src.spreadDegrees) / 360f);
                dst.shapeScale = new ZUIValue(Mathf.Max(2f, Peak(src.originInset) + 2f));   // placement near centre; length reaches out
                dst.shapeRotation = new ZUIValue(Peak(src.baseAngleDeg));
                SetupWindowTiming(src, dst, spec);
                outList.Add(dst);
                warnings.Add($"{label}: star Bars → a ring of {dst.swarmCount} outward Streaks. Each arm's multi-bar COMB (barCount/spacing/taper) collapses to one streak per arm.");
                return;
            }

            // The row: a Line swarm of Streaks. Emit a mirrored twin when barMirror is on.
            AddBarRow(src, spec, globalMods, warnings, half, false, outList, label);
            if (src.barMirror)
            {
                AddBarRow(src, spec, globalMods, warnings, half, true, outList, label + " (mirror)");
                warnings.Add($"{label}: barMirror → a second mirrored Streak-row layer.");
            }
        }

        static void AddBarRow(Layer src, PyreAsset spec, List<PyreModifier> globalMods, List<string> warnings,
                              float half, bool mirror, List<PyrePlusLayer> outList, string label)
        {
            var dst = BuildBaseLayer(src, spec, globalMods, warnings, label);
            dst.shapeForm = ShapeForm.Streak;
            MapStreakBody(src, dst);

            int total = 2 * Mathf.Max(0, Mathf.RoundToInt(Peak(src.barCount))) + 1;
            float combHalf = Mathf.Max(1f, Peak(src.barCount)) * Mathf.Max(1f, Peak(src.barSpacing))
                             * Mathf.Max(1f, Peak(src.barWidth)) + Mathf.Max(1f, Peak(src.barWidth));

            dst.swarmEnabled = true;
            dst.swarmShapeKind = SwarmShapeKind.Line;        // the row is a straight 1-D line of streaks
            dst.swarmSpawnMode = SwarmSpawnMode.Area;        // Line is 1-D, independent of Area/Path
            dst.swarmCount = Mathf.Clamp(total, 2, 256);
            dst.shapeScale = new ZUIValue(Mathf.Max(2f, combHalf));   // Line endpoints at ±shapeScale
            dst.swarmOrient = SwarmOrient.None;

            // barTaper: +1 centre-longest → edges short (a flame). The Line index runs 0..1 across the row, so a
            // curve PEAKING at the middle (0.5) tapers centre-vs-edge. streakScaleLengthOnly keeps every bar the
            // same WIDTH — only the length graduates (equal-width bars of graduated length = Pyre's flame).
            dst.streakScaleLengthOnly = true;
            dst.swarmScaleByIndex = TaperCurve(Peak(src.barTaper));

            float angle = Peak(src.baseAngleDeg) + Peak(src.barAngleDeg);
            if (mirror) angle = Peak(src.baseAngleDeg) - Peak(src.barAngleDeg);
            dst.shapeRotation = new ZUIValue(angle);

            SetupWindowTiming(src, dst, spec);
            // barStagger folds into the spawn-timing spread (SetupWindowTiming reads spawnStagger; nudge it up so the
            // centre-first appearance reads as a spread over the window).
            if (Peak(src.barStagger) > 0.001f)
                dst.swarmSpawnTiming = StaggerTiming(src, spec, Peak(src.barStagger));

            if (src.barDecay == BarDecay.Dissolve)
                warnings.Add($"{label}: Bars Dissolve decay not ported (cross-index alpha front) — used the Contract length envelope instead.");
            outList.Add(dst);
        }

        static void MapStreakBody(Layer src, PyrePlusLayer dst)
        {
            dst.streakWidth = CloneVal(src.barWidth);
            dst.streakLength = CloneVal(src.barForward);
            dst.streakAnchor = Mathf.Clamp01(Peak(src.barBackwardFrac));
            float soft = Mathf.Clamp01(Peak(src.barSoftness));
            dst.edgeSoftness = soft;
            dst.streakSoftTip = soft;
        }

        // ── swarm setup ───────────────────────────────────────────────────────────────
        // Area / Ring scatter → a swarm; a single (count 1, Area) shape stays swarm-off (one centred particle).
        static void SetupScatterSwarm(Layer src, PyrePlusLayer dst, PyreAsset spec, List<string> warnings, string label)
        {
            int count = Mathf.Max(0, Mathf.RoundToInt(Peak(src.count)));
            float half = Mathf.Max(1, spec.canvasSize) * 0.5f;

            if (src.scatterMode == ScatterMode.Area && count <= 1)
            {
                // One centred shape. No swarm — PyrePlus draws a single particle over the whole timeline. A
                // sub-window or a per-shape offset can't be reproduced without a swarm, so warn if either is set.
                dst.swarmEnabled = false;
                if (!IsFullWindow(src, spec))
                    warnings.Add($"{label}: single-shape [start {src.startFrame}, end {src.endFrame}] window can't be sub-timed without a swarm — the shape spans the whole animation.");
                if (Peak(src.positionX) != 0f || Peak(src.positionY) != 0f)
                    warnings.Add($"{label}: single-shape position offset dropped (only a swarm carries per-shape offsets).");
                return;
            }

            dst.swarmEnabled = true;
            dst.swarmCount = Mathf.Clamp(Mathf.Max(2, count), 2, 256);
            dst.shapeScale = ScaleVal(src.spawnRadius, half);   // Pyre spawnRadius is 0..1 of the canvas half-size
            dst.shapeOffsetX = CloneVal(src.positionX);
            dst.shapeOffsetY = CloneVal(src.positionY);

            if (src.scatterMode == ScatterMode.Ring)
            {
                dst.swarmSpawnMode = SwarmSpawnMode.Path;
                dst.swarmShapeKind = SwarmShapeKind.Circle;
                dst.swarmEvenPath = src.ringOrder == RingOrder.Sequential;
                dst.swarmPathSpread = Mathf.Clamp01(Peak(src.ringArcDegrees) / 360f);
                dst.shapeRotation = CloneVal(src.ringStartAngle);           // where the ring arc begins
                dst.swarmScale = CloneVal(src.ringExpand);                  // live ring bloom
                dst.swarmOrient = src.ringAlignRotation ? SwarmOrient.Outward : SwarmOrient.None;
            }
            else
            {
                dst.swarmSpawnMode = SwarmSpawnMode.Area;
                dst.swarmShapeKind = SwarmShapeKind.Circle;
            }
            SetupWindowTiming(src, dst, spec);
        }

        // A ring blooming out over life for one RoseRing → a Path/Circle swarm placed on that ring's radius, its
        // own count/size-scale/birth/life driving the swarm.
        static void SetupRoseRingSwarm(Layer src, PyrePlusLayer dst, PyreAsset spec, RoseRing ring)
        {
            float half = Mathf.Max(1, spec.canvasSize) * 0.5f;
            dst.swarmEnabled = true;
            dst.swarmCount = Mathf.Clamp(Mathf.Max(2, ring.count), 2, 256);
            dst.swarmSpawnMode = SwarmSpawnMode.Path;
            dst.swarmShapeKind = SwarmShapeKind.Circle;
            dst.swarmEvenPath = src.ringOrder == RingOrder.Sequential;
            dst.swarmPathSpread = Mathf.Clamp01(Peak(src.ringArcDegrees) / 360f);
            dst.shapeScale = new ZUIValue(Mathf.Max(1f, ring.radius * half));
            dst.shapeRotation = CloneVal(src.ringStartAngle);
            dst.swarmScale = CloneVal(src.ringExpand);
            dst.swarmOrient = src.ringAlignRotation ? SwarmOrient.Outward : SwarmOrient.None;
            // The ring's own size-scale multiplies the layer size for just this ring's discs.
            dst.size = ScaleVal(src.size, Mathf.Max(0.01f, ring.sizeScale));
            // Birth/life of the ring → spawn-together at birth, live for `life` of the timeline.
            dst.swarmSpawnTiming = new ZUIValue(Mathf.Clamp01(ring.birth));
            dst.swarmParticleLife = Mathf.Clamp(ring.life, 0.05f, 1f);
        }

        // Reproduce a Pyre layer's [startFrame, endFrame] window through the swarm's spawn-timing + particle-life
        // (PyrePlus has no per-layer frame window). Spawn-stagger spreads spawns across the window; syncDeath maps
        // to swarmDieTogether.
        static void SetupWindowTiming(Layer src, PyrePlusLayer dst, PyreAsset spec)
        {
            int frames = Mathf.Max(1, spec.frameCount);
            float last = Mathf.Max(1, frames - 1);
            float t0 = Mathf.Clamp01(src.startFrame / last);
            float t1 = Mathf.Clamp01(src.endFrame / last);
            if (t1 < t0) t1 = t0;
            float winLen = Mathf.Clamp(t1 - t0, 0.05f, 1f);

            dst.swarmParticleLife = winLen;
            if (src.spawnStagger > 0.001f)
                dst.swarmSpawnTiming = StaggerTiming(src, spec, src.spawnStagger);
            else
                dst.swarmSpawnTiming = new ZUIValue(t0);   // all spawn together at the window start
            dst.swarmDieTogether = src.syncDeath;
        }

        // A spawn-timing curve spreading the N spawns from the window start across a `stagger` fraction of the window.
        static ZUIValue StaggerTiming(Layer src, PyreAsset spec, float stagger)
        {
            int frames = Mathf.Max(1, spec.frameCount);
            float last = Mathf.Max(1, frames - 1);
            float t0 = Mathf.Clamp01(src.startFrame / last);
            float t1 = Mathf.Clamp01(src.endFrame / last);
            if (t1 < t0) t1 = t0;
            float span = Mathf.Clamp01(stagger) * Mathf.Max(0f, t1 - t0);
            return Curve2(0f, t0, 1f, Mathf.Clamp01(t0 + span));
        }

        // ── colour → ZuiFill ────────────────────────────────────────────────────────
        static ZuiFill MakeFill(Layer src, List<string> warnings, string label)
        {
            var grad = CloneGradient(src.colorOverLife);
            switch (src.colorMode)
            {
                case ColorMode.Fill:
                    return new ZuiFill
                    {
                        mode = ZuiFill.Mode.Radial, gradient = grad,
                        center = new Vector2(Mathf.Clamp(Peak(src.gradientOffsetX), -1f, 1f), Mathf.Clamp(Peak(src.gradientOffsetY), -1f, 1f)),
                        zoom = Mathf.Max(0.05f, Peak(src.colorFlowZoom)),
                    };
                case ColorMode.FlowingFill:
                    warnings.Add($"{label}: Flowing-fill colour → a static Radial fill (the flow-scroll over life is dropped).");
                    return new ZuiFill
                    {
                        mode = ZuiFill.Mode.Radial, gradient = grad,
                        center = new Vector2(Mathf.Clamp(Peak(src.gradientOffsetX), -1f, 1f), Mathf.Clamp(Peak(src.gradientOffsetY), -1f, 1f)),
                        zoom = Mathf.Max(0.05f, Peak(src.colorFlowZoom)),
                    };
                case ColorMode.NoiseFill:
                    warnings.Add($"{label}: Noise-fill colour → a Noise texture fill (domain warp / rotation / drift / bands not carried over).");
                    return new ZuiFill
                    {
                        texture = ZuiFill.TextureKind.Noise, gradient = grad,
                        zoom = Mathf.Max(0.05f, Peak(src.noiseZoom) / 20f),   // Pyre noiseZoom is a pixel feature size; ZuiFill zoom is a unit scale
                    };
                default: // OverLife
                    return new ZuiFill { mode = ZuiFill.Mode.OverLife, gradient = grad };
            }
        }

        // ── modifiers ─────────────────────────────────────────────────────────────────
        static void CopyModifiers(List<PyreModifier> list, PyrePlusLayer dst, List<string> warnings, string label)
        {
            if (list == null) return;
            foreach (var m in list)
            {
                if (m == null) continue;
                if (m is EdgeModifier)
                {
                    warnings.Add($"{label}: '{Safe(m)}' edge modifier dropped (PyrePlus's disc raster has no edge-warp stage).");
                    continue;
                }
                if (m is SimulationModifier sim)
                {
                    if (dst.simulationModifier == null) dst.simulationModifier = (SimulationModifier)sim.Clone();
                    else warnings.Add($"{label}: a second simulation modifier ('{Safe(m)}') was dropped (one per-layer slot).");
                    continue;
                }
                dst.modifiers.Add(m.Clone());   // Geometry / Pixel / Post — all apply in PyrePlus
            }
        }

        static string Safe(PyreModifier m) { try { return m.DisplayName; } catch { return m.GetType().Name; } }

        // ── ZUIValue / gradient helpers ────────────────────────────────────────────────
        static ZUIValue CloneVal(ZUIValue v)
        {
            var c = new ZUIValue();
            if (v != null) c.CopyFrom(v);
            return c;
        }

        // Clone then multiply the whole value (static / min-max / every curve point / y-range) by k — used to
        // convert Pyre's 0..1 normalized radii into PyrePlus's pixel scales.
        static ZUIValue ScaleVal(ZUIValue v, float k)
        {
            var c = CloneVal(v);   // CopyFrom deep-copied the points, so mutating them can't touch the source
            c.staticValue *= k;
            c.min *= k; c.max *= k;
            c.yMin *= k; c.yMax *= k;
            if (c.points != null)
                foreach (var p in c.points) p.value *= k;
            return c;
        }

        // A representative plain number: the constant, the max of a Min-Max spread, or the highest curve point —
        // the "how big does this get" value, used to size swarms/scales/counts.
        static float Peak(ZUIValue v)
        {
            if (v == null) return 0f;
            switch (v.mode)
            {
                case ZUIValue.Mode.Static: return v.staticValue;
                case ZUIValue.Mode.MinMax: return Mathf.Max(v.min, v.max);
                case ZUIValue.Mode.Curve:
                    float m = 0f;
                    if (v.points != null) foreach (var p in v.points) m = Mathf.Max(m, p.value);
                    return m;
                default: return v.staticValue;
            }
        }

        // A two-point curve (t0,v0)→(t1,v1) in a 0..1 y-range.
        static ZUIValue Curve2(float t0, float v0, float t1, float v1)
        {
            var v = new ZUIValue { mode = ZUIValue.Mode.Curve, yMin = 0f, yMax = 1f };
            v.points.Clear();
            v.points.Add(new ZUIEnvelopePoint(t0, v0));
            v.points.Add(new ZUIEnvelopePoint(t1, v1));
            return v;
        }

        // barTaper as a centre-peaked index curve: edges at (1−taper), centre at 1 — equal-width bars of graduated
        // length (a flame silhouette) when driving streakScaleLengthOnly.
        static ZUIValue TaperCurve(float taper)
        {
            float edge = Mathf.Clamp01(1f - Mathf.Clamp(taper, -1f, 1f));
            var v = new ZUIValue { mode = ZUIValue.Mode.Curve, yMin = 0f, yMax = 1f };
            v.points.Clear();
            v.points.Add(new ZUIEnvelopePoint(0f, edge));
            v.points.Add(new ZUIEnvelopePoint(0.5f, 1f));
            v.points.Add(new ZUIEnvelopePoint(1f, edge));
            return v;
        }

        static bool IsFullWindow(Layer src, PyreAsset spec)
        {
            int last = Mathf.Max(0, spec.frameCount - 1);
            return src.startFrame <= 0 && src.endFrame >= last;
        }

        static Gradient CloneGradient(Gradient g)
        {
            if (g == null) return ZuiFill.DefaultGradient();
            var n = new Gradient();
            n.SetKeys(g.colorKeys, g.alphaKeys);
            n.mode = g.mode;
            return n;
        }
    }
}
