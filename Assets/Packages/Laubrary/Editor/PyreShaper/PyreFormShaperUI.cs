// PyreFormShaperUI — the Fill / Alpha / Frames rows a hosted PyreForm needs, above its reflected dial dump (T-0202).
//
// ── what this closes ─────────────────────────────────────────────────────────────────────────────────────
// A hosted form's dials were drawn by the window's reflected dump over the FORM object
// (ShaperWindow.Sections.cs:1056-1057, 1117-1142). That is the right default for objects declaring ~775 fields
// between them and it stays the default here — this drawer runs it verbatim. But three of the things Pyre's own
// Shape section shows for a hosted form are NOT declared on the form: the layer's Shape Fill, the layer's Alpha
// envelope and the clip's frame count (PyreWindow.cs:1263-1267). A hosted form has no layer, so T-0202 moved
// those three onto PyreFormCompositeSource — and a field on the SOURCE is exactly what the reflected dump does
// not look at. Without this drawer they would be authored data with no control.
//
// ── why a whole drawer rather than a window edit ─────────────────────────────────────────────────────────
// Same reason PyreLayerShaperUI is a drawer: the registry (ShaperCompositeSourceUICatalog) is the published way
// for a generator family to bring its own card, and Shaper's core editor assembly is not allowed to learn this
// family by name. The cost is that a drawer replaces the reflected dump rather than sitting beside it, so the
// dump is reproduced below — with the window's own options, so the two cannot look different.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Laubrary.Pyre;
using Laubrary.Shaper;
using Laubrary.Shaper.Editor;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.PyreShaper.Editor
{
    [InitializeOnLoad]
    public static class PyreFormShaperUI
    {
        static PyreFormShaperUI()
        {
            ShaperCompositeSourceUICatalog.Register(new Drawer());
        }

        sealed class Drawer : IShaperCompositeSourceUI
        {
            public bool CanDraw(IShaperCompositeSource source)
                => source is PyreFormCompositeSource s && s.form != null;

            public void Build(ShaperSourceUIContext ctx, IShaperCompositeSource source)
            {
                var src = (PyreFormCompositeSource)source;

                // A source authored before T-0202 deserializes with these null, which reads on screen as a
                // control with nothing behind it. Repairing here — inside the window's own Change, so it is one
                // undoable step — is the same repair PyreLayerShaperUI makes for a null layer.
                if (src.shapeFill == null || src.alpha == null)
                    ctx.Change(() =>
                    {
                        src.shapeFill ??= PyreFormCompositeSource.DefaultFill();
                        src.alpha ??= PyreFormCompositeSource.DefaultAlpha();
                    });

                var box = Z.BoxKeyed(src.SourceLabel,
                    "The paint and opacity this generator is hosted with — the same rows Pyre's own Shape "
                    + "section shows above a form's dials. Its own frame count is no longer authored here: it "
                    + "follows the document's own Frames, so a hosted generator has exactly one lifetime.",
                    "shaper.window.composite.pyreform");

                // T-0254 — "Generator frames" is GONE, the same treatment T-0204 gave
                // PyreLayerCompositeSource.frames: it used to author a SECOND, independent frame axis that
                // could silently disagree with the document's own Frames. It is now silently kept in lock-step
                // with the document instead of exposed as a dial — `frames` always mirrors ctx.FrameCount,
                // which is what makes `phase01 · (frames − 1)` land on the SAME frame index the document's own
                // clock is on. No Undo entry for this: it is bookkeeping that keeps two numbers equal, not an
                // authored edit.
                if (ctx.FrameCount > 0 && src.frames != ctx.FrameCount) { src.frames = ctx.FrameCount; ctx.Touch(); }

                // Pyre draws no Fill row for a form that carries its own ramps (UsesFill == false,
                // PyreWindow.cs:1263) because the control would be dead. Same test, same reason: of the nine,
                // only Inferno and Fork Blast read it.
                if (src.form.UsesFill)
                    box.Add(Z.Fill("Fill", src.shapeFill,
                        $"The {src.form.DisplayName} generator's colour source. {src.form.Description}",
                        ctx.Touch, () => RecordUndo(ctx),
                        new ZuiFillControl.Options().WithWidth(190f).WithGrow(2.2f)));

                box.Add(Z.Value("Alpha", src.alpha, new ZuiValueControl.Options
                {
                    absMin = 0f, absMax = 1f,
                    hideCurveTiming = true, hideCurveRange = true, hideLiveReadout = true,
                    controlWidth = 170f, grow = true,
                    frameCount = Mathf.Max(1, src.frames),
                }, "Overall opacity across this generator's life — multiplied into the picture it paints.",
                   ctx.Touch, () => RecordUndo(ctx)));

                // The form's own dials, drawn exactly as the window's default path draws them
                // (ShaperWindow.Sections.cs:1117-1142) — same box key, same options — so replacing the dump with
                // this drawer changes nothing about how a form's ~775 fields appear.
                var dials = Z.BoxKeyed(src.SourceLabel + " dials",
                    "Every dial this generator declares, read straight off the generator itself — so it cannot "
                    + "drift out of date as the generator changes. A dial set to a curve is read at each frame's "
                    + "own phase, so animating one animates the picture.",
                    "shaper.window.composite.dials");
                var host = new VisualElement();
                // T-0337 — a nested settings object cannot see the form that owns it, and one guard needs to
                // (a population's Swirl follow is a share of the form's own Swirl). Named for exactly the span
                // of this synchronous build and cleared in a finally, so nothing outside a card ever reads it.
                _reflectingForm = src.form;
                try
                {
                ZuiReflect.FlowFields(host, src.form, new ZuiReflect.Options
                {
                    OnBeforeChange = () => RecordUndo(ctx),
                    OnChanged = ctx.Touch,
                    OnStructureChanged = ctx.Rebuild,
                    ControlWidth = 140f,
                    ReorderFields = OrbNoseAxisReorder,
                    TooltipFor = DialTooltip,
                    InertReason = DialInertReason,
                    IsInertGuard = IsDialInertGuard,
                    RampHonouredKnobs = RampHonouredKnobs,
                    // T-0279 — a [PyreSwarmOnly] dial only ever reads `ctx.swarm` (PyreForm.Prepare/Render), and
                    // this bridge's own Render (above) always calls form.Render with a PyreFormCtx built from
                    // `null, null, null` for the swarm slots — a composite node hosts exactly one form with no
                    // swarm wired in at all, unlike Pyre's own window, which only hides these dials when the
                    // layer's swarm is OFF (PyreWindow.Forms.cs:127) because there it CAN be turned on. Measured
                    // 0 changed pixels on every one of them (swarmSize on Orb/Torch/Jet/RadialJet/ExplosiveJet/
                    // ArcBurst, InfernoForm's four blast-variation dials) at every value tried — dead by
                    // construction here, not by authoring, so the dial is absented rather than drawn inert.
                    Skip = f => System.Attribute.IsDefined(f, typeof(PyreSwarmOnlyAttribute)),
                });
                }
                finally { _reflectingForm = null; }
                dials.Add(host);
                box.Add(dials);

                ctx.Body.Add(box);
            }

            // Complete-object, never RecordObject: a document is a graph of [SerializeReference]s and
            // RecordObject does not snapshot them, so undo would null them (T-0198, ShaperCompositeSourceUI.cs).
            static void RecordUndo(ShaperSourceUIContext ctx)
            {
                if (ctx.UndoTarget != null)
                    Undo.RegisterCompleteObjectUndo(ctx.UndoTarget, "Edit Shaper Document");
            }

            // ── OrbForm's Nose X / Axis Y (T-0220) ─────────────────────────────────────────────────────────
            // Owner: "are these just placing the shape in the frame? Transform already does this." They are
            // NOT — off-swarm they are the intrinsic composition point the wake trails behind (OrbForm.cs's
            // own doc: "SWARM: off = one orb at Nose X / Axis Y"), read only by the generator's own program,
            // never by ShaperTransformBlock. But OrbForm declares them second and third (right after
            // `variant`, ahead of Radius/Wake), so the reflected dump drew them at the top of the card, beside
            // Transform's own Translate row one section up — exactly where they'd read as a duplicate. Can't
            // fix the declaration order without editing OrbForm.cs (Pyre's file, read-only to this
            // programme); fixed here instead, purely at draw time, by field name — a no-op on every form that
            // doesn't declare all three.
            static readonly string[] NoseAxisFields = { "noseX", "axisY" };

            static FieldInfo[] OrbNoseAxisReorder(FieldInfo[] fields)
            {
                int noseIdx = Array.FindIndex(fields, f => f.Name == "noseX");
                int axisIdx = Array.FindIndex(fields, f => f.Name == "axisY");
                int radiusIdx = Array.FindIndex(fields, f => f.Name == "radius");
                if (noseIdx < 0 || axisIdx < 0 || radiusIdx < 0) return fields;

                var nose = fields[noseIdx];
                var axis = fields[axisIdx];
                var rest = new List<FieldInfo>(fields.Length);
                foreach (var f in fields)
                    if (f.Name != "noseX" && f.Name != "axisY") rest.Add(f);

                int insertAt = rest.FindIndex(f => f.Name == "radius") + 1;   // right after Radius, before Wake
                rest.Insert(insertAt, nose);
                rest.Insert(insertAt + 1, axis);
                return rest.ToArray();
            }

            static string OrbNoseAxisTooltip(FieldInfo f)
            {
                if (Array.IndexOf(NoseAxisFields, f.Name) < 0) return null;
                var attr = (TooltipAttribute)Attribute.GetCustomAttribute(f, typeof(TooltipAttribute));
                return "Within the orb's own frame: " + (attr?.tooltip ?? f.Name);
            }

            // ── conditional dials say what they are waiting for (T-0280) ────────────────────────────────────
            // A dial the engine reads only inside an `if` looks broken when its gate is shut: it is a live
            // control that repaints nothing, and the author has no way to tell that apart from a bug. T-0280
            // swept all four families curve-aware at their own native frame across three phases and found 54
            // such dials — every one measured dead at the variant's own defaults and measured LIVE the moment
            // the guard named below was opened, so the condition is the engine's own `if`, not a guess.
            //
            // The condition is APPENDED to the dial's own tooltip rather than drawn as chrome: ZuiReflect
            // exposes no per-field disable hook, and inventing one would be a new control this programme is
            // not allowed to add. The companion is named by its ZUILabel, not its field name, because that is
            // what the author sees on the card next to it.
            //
            // Keyed by declaring type as well as name because the same name carries different conditions on
            // different engines — JetSettings.pulseN gates on Surge depth, TorchSettings.pulseN on Surge jump.
            static readonly Dictionary<string, string> ConditionOf = new Dictionary<string, string>
            {
                // JetSettings — shared by Jet, Radial Jet and Explosive Jet (PyreJetEngine.cs:585-628)
                { "JetSettings.pulseN",      "Nothing until Surge depth is above 0." },
                { "JetSettings.pulseDepth",  "Nothing until Surges is above 0." },
                { "JetSettings.sweepN",      "Nothing until Sweep angle is above 0." },
                { "JetSettings.shockN",      "Nothing until Shock amt. is above 0." },
                { "JetSettings.shockDepth",  "Nothing until Shock count is above 0." },
                { "JetSettings.ringK",       "Nothing until Ring count is above 0." },
                { "JetSettings.ringR0",      "Nothing until Ring count is above 0." },
                { "JetSettings.ringGrow",    "Nothing until Ring count is above 0." },
                { "JetSettings.ringLife",    "Nothing until Ring count is above 0." },
                { "JetSettings.ringReach",   "Nothing until Ring count is above 0." },
                { "JetSettings.ringAmp",     "Nothing until Ring count is above 0." },
                { "JetSettings.rootAmp",     "Nothing until Lump size is above 0 — there is no lump to light." },

                // RadialJetSettings (RadialJetProgram.cs:128-134, 189)
                { "RadialJetSettings.lobes",     "Nothing until Tongue clump or Tongue kick is above 0." },
                { "RadialJetSettings.lobeDepth", "Nothing until Tongue count is above 0." },
                { "RadialJetSettings.lobeKick",  "Nothing until Tongue count is above 0." },
                { "RadialJetSettings.rootK",     "Nothing until Burn radius is above 0." },
                { "RadialJetSettings.ringFlat",  "Nothing until Ring count is above 0 — there are no rings to turn face-on." },

                // ExplosiveJetSettings (ExplosiveJetProgram.cs:468-473, 683-706, 843-871)
                { "ExplosiveJetSettings.lobes",     "Nothing until Tongue clump or Tongue kick is above 0." },
                { "ExplosiveJetSettings.lobeDepth", "Nothing until Tongue count is above 0." },
                { "ExplosiveJetSettings.lobeKick",  "Nothing until Tongue count is above 0." },
                { "ExplosiveJetSettings.rootK",     "Nothing until Burn radius is above 0 AND the Blast schedule is empty — a scheduled detonation has no standing source, so its seat is one lump per blast." },
                { "ExplosiveJetSettings.ringFlat",  "Nothing until Ring count is above 0 — there are no rings to turn face-on." },
                { "ExplosiveJetSettings.ringArc",   "Nothing until Rings face-on is on and Ring count is above 0." },
                { "ExplosiveBlast.share",           "Nothing until the Blast schedule holds two or more blasts — a lone blast owns every slot whatever its share." },

                // TorchSettings (PyreTorch.cs:581, 589-594, 638-657)
                { "TorchSettings.lashK",    "Nothing until Whip amount is above 0." },
                { "TorchSettings.lashWave", "Nothing until Whip amount is above 0." },
                { "TorchSettings.lashPh",   "Nothing until Whip amount is above 0." },
                { "TorchSettings.pulseN",   "Nothing until Surge jump, Surge glow or Heat lump is above 0 — the surge is computed either way, but nothing reads it." },
                { "TorchSettings.pulsePh",  "Nothing until Surge jump, Surge glow or Heat lump is above 0." },
                { "TorchSettings.bulgeW",   "Nothing until Heat lump is above 0." },
                { "TorchSettings.curlX",    "Nothing until Curl warp is above 0." },
                { "TorchSettings.curlY",    "Nothing until Curl warp is above 0." },

                // OrbForm (PyreOrb.cs:654-668) — content-dependent rather than gated, but the same trap
                { "OrbForm.despeckle",      "Only drops pixels that are BOTH faint and isolated. Measured over Emberdrift, Wisp, Coronal and Membrane at three seeds: those variants never produce one, so this changes nothing on them; on Voltcore it clears 5-10 px a frame." },
                { "OrbForm.despeckleBelow", "Only reaches pixels that are also isolated, so it changes nothing on the variants that have none (Emberdrift, Wisp, Coronal, Membrane) — measured." },

                // ── T-0336 — the three forms this table never covered ────────────────────────────────────
                // T-0280/T-0281 wrote the sentences and the greying for the Jet family, Torch and Orb. A
                // declared-dial sweep of the three LARGEST forms (round 20 §2) then measured, at frames 2-4
                // of 8 where all three actually paint, fifteen more dials that move EXACTLY 0 pixels at their
                // own form's factory defaults and come alive the moment ONE named sibling is raised. Each
                // condition below is the engine's own `if`, cited, not an inference from the measurement.

                // PlasmaBloomForm (PyrePlasmaBloom.cs:324, 329, 405)
                { "PlasmaBloomForm.biasDir",  "Nothing until Bias Amount is above 0." },
                { "PlasmaBloomForm.biasK",    "Nothing until Bias Amount is above 0." },
                { "PlasmaBloomForm.halfDir",  "Nothing until Gate Amount is above 0." },
                { "PlasmaBloomForm.halfSoft", "Nothing until Gate Amount is above 0." },
                { "PlasmaBloomForm.halfK",    "Nothing until Gate Amount is above 0." },
                { "PlasmaBloomForm.lobeMode", "Nothing until Lobe Amount is above 0 — the lobe gate is multiplied by it, so at 0 the whole term drops out." },
                { "PlasmaBloomForm.lobePow",  "Nothing until Lobe Amount is above 0." },
                { "PlasmaBloomForm.lobePh",   "Nothing until Lobe Amount is above 0." },

                // ForkBlastForm (PyreForkBlast.cs:215, 305)
                { "ForkBlastForm.aim",       "Nothing while Spread angle is at 180: a full circle has no facing, so the arc's centre direction is dropped outright." },
                { "ForkBlastForm.gobSizePx", "Nothing until Gob count is above 0." },
                { "ForkBlastForm.gobReach",  "Nothing until Gob count is above 0." },
                { "ForkBlastForm.gobSwell",  "Nothing until Gob count is above 0." },
                { "ForkBlastForm.gobLife",   "Nothing until Gob count is above 0." },
                { "ForkBlastForm.gobAmount", "Nothing until Gob count is above 0." },
                { "ForkBlastForm.gobTiming", "Nothing until Gob count is above 0." },

                // ── T-0337 — the twenty round 20 named but did not trace ─────────────────────────────────
                // Thirteen of them turned out to be guarded by a named sibling and are greyed below; the
                // other seven are LIVE and get a sentence only, because what makes them look dead is the
                // frame the picture is sampled at, the seed, or the Fill above them — never a guard.

                // ArcBurstForm — the deep-ghost pair is gated by the ACTIVE arc pattern's own Deep ghost
                // share (PyreArcBurst.cs:403, `if (rng.Random() < deepP) return rng.Uniform(deepLo, deepHi);`),
                // which ships at 0 on Bolt, Cage, Stipple and Pinch and does not exist at all on Crown and
                // Lattice — those two never call Ghost (PyreArcBurst.cs:639, 688).
                { "ArcBurstForm.ghostDeepLo",
                  "Nothing until Deep ghost, in the arc pattern's own Ghosts group, is above 0 — and the Crown and Lattice patterns draw no ghosts at all." },
                { "ArcBurstForm.ghostDeepHi",
                  "Nothing until Deep ghost, in the arc pattern's own Ghosts group, is above 0 — and the Crown and Lattice patterns draw no ghosts at all." },
                // Not gated, content-dependent — the OrbForm.despeckle shape. KeepHue is unconditional
                // (PyreArcBurst.cs:273) but only ever sees a stroke that came out a ghost (:402).
                { "ArcBurstForm.keepHueFloor",
                  "Only reaches strokes that came out as ghosts, which Ghost share decides. Measured on the Bolt pattern at its shipped five trunks: at that seed not one stroke is a ghost, so this changes nothing there; with six more trunks it moves 798 pixels." },
                // Live, but late: the escape speeds are multiplied by a ramp that is 0 until Net breaks at
                // (PyreArcBurst.cs:725-727), so every frame before it is identical whatever they say.
                { "LatticeSettings.flyLo", "Only acts after Net breaks at — every frame before it looks the same whatever this says." },
                { "LatticeSettings.flyHi", "Only acts after Net breaks at — every frame before it looks the same whatever this says." },
                // Live, but later still, and short documents can step over the window entirely: Cool is
                // Pow(1 − Ramp(t, coolStart, 1), k) (PyreArcBurst.cs:274), which is 1 at or before Cool start
                // and exactly 0 at the last frame — so only a frame strictly between them reads this at all.
                { "CageSettings.coolK",
                  "Only shapes the fade between Cool start and the last frame, and it is exactly 0 on the last frame itself — so a short document can hold no frame that reads this. Measured: nothing at all over 8 frames, 455 pixels over 16." },

                // PlasmaBloomForm — the drift trio sits behind the mutual drift pair. DriftOf returns early
                // while Drift X and Drift Y are both 0 (PyrePlasmaBloom.cs:206) and multiplies by Drift
                // Amount on the next line (:207), so BOTH have to be open before any of the three is read.
                { "PlasmaBloomForm.driftEase", "Nothing until Drift Amount is above 0 and Drift X or Drift Y is non-zero — with no drift there is nothing to ease." },
                { "PlasmaBloomForm.driftLin",  "Nothing until Drift Amount is above 0 and Drift X or Drift Y is non-zero." },
                { "PlasmaBloomForm.driftLag",  "Nothing until Drift Amount is above 0 and Drift X or Drift Y is non-zero — the wake lags behind a source that has not moved." },
                // The lobe/plume cluster. `plume` is Plume AND at least two lobes (PyrePlasmaBloom.cs:327-328);
                // the lobe gate is only computed at all inside `if (lobes)` (:383); and both branches that read
                // it are scaled by an amount that ships at 0 — Plume Amount (:403) and Lobe Amount (:405) —
                // so with neither raised the two modes paint the identical picture.
                { "PlasmaBloomForm.mode",
                  "Nothing until Lobe Count is 2 or more and either Lobe Amount or Plume Amount is above 0 — below that, Plume paints exactly what Bloom does." },
                { "PlasmaBloomForm.gateGain",
                  "Nothing until Lobe Count is 2 or more and either Lobe Amount or Plume Amount is above 0 — the lobe gate this sharpens is not read otherwise." },
                { "PlasmaBloomForm.plumeAmp",
                  "Nothing until Lobe Count is 2 or more — the tongues are cut out of the lobes, so with fewer there are none to brighten." },
                { "PlasmaBloomForm.plumeReach", "Nothing until Lobe Count is 2 or more and Plume Amount is above 0 — there is no tongue to reach." },
                { "PlasmaBloomForm.plumeW",     "Nothing until Lobe Count is 2 or more and Plume Amount is above 0 — there is no tongue to widen." },
                { "PlasmaBloomForm.plumeVary",
                  "Nothing until Lobe Count is 2 or more, Plume Amount is above 0 and Front Warp is above 0 — the unevenness is read off the warp noise." },
                // Every population's "Swirl follow" is its share of the form's own Swirl, multiplied by it
                // (PyrePlasmaBloom.cs:245), and that ships at 0.
                { "PlasmaPopulation.swirl",
                  "Nothing until Swirl, on the generator itself, is non-zero — this is only that turn's share for this population." },

                // ForkBlastForm — neither of these is gated by a dial. The flash is real but brief, and the
                // solidity exponent is real but reads the Fill drawn above these dials, not a sibling.
                { "ForkBlastForm.flash",
                  "Its whole life is the first eighth of a blast's own (PyreForkBlast.cs:418), so a short document can step straight over it: measured, nothing at all over 8 frames and 936 pixels over 16." },
                { "ForkBlastForm.opacity",
                  "It does nothing at all while that ceiling is 1, which is what the Fill above ships as: give the Fill an alpha below 1 and this takes hold." },
            };

            static string DialTooltip(FieldInfo f)
            {
                string nose = OrbNoseAxisTooltip(f);
                if (nose != null) return nose;
                if (f.DeclaringType == null) return null;
                if (!ConditionOf.TryGetValue(f.DeclaringType.Name + "." + f.Name, out var why)) return null;
                var attr = (TooltipAttribute)Attribute.GetCustomAttribute(f, typeof(TooltipAttribute));
                return string.IsNullOrEmpty(attr?.tooltip) ? why : attr.tooltip + " " + why;
            }

            // ── grey the 54 (T-0281) ──────────────────────────────────────────────────────────────────────
            // ConditionOf above only ever appends TEXT — it cannot disable a control, because it has no idea
            // what the guard's CURRENT value is. This table adds that: for each conditional dial that CAN be
            // checked safely, a live read of its guard sibling(s) on the SAME owner instance, straight off the
            // engine's own `if` (the file:line citations in ConditionOf's comments).
            //
            // "Safely" excludes two real traps found while building this table, both confirmed against the
            // engine source, not guessed:
            //   • MUTUAL pairs — JetSettings.pulseN needs pulseDepth>0 and pulseDepth needs pulseN!=0
            //     (PyreJetEngine.cs:587), and shockN/shockDepth the same (PyreJetEngine.cs:601). Disabling
            //     BOTH sides of a mutual pair the moment the OTHER is at its shipped-zero default would grey
            //     them PERMANENTLY — neither could ever be dragged open again through this drawer. Left
            //     enabled; T-0280's tooltip text still names the condition.
            //   • CIRCULAR triples — RadialJet/ExplosiveJet's lobes/lobeDepth/lobeKick: lobes is gated on
            //     (lobeDepth OR lobeKick), and both of THOSE are gated on lobes (RadialJetProgram.cs:128,133;
            //     ExplosiveJetProgram.cs:468,472). All three ship at 0, so all three would grey each other
            //     forever. Same treatment: left enabled, tooltip-only.
            //   • ExplosiveJetSettings.rootK's second condition ("the Blast schedule is empty") and
            //     ExplosiveBlast.share's ("the schedule holds 2+ blasts") both read a LIST on a different
            //     object than the field itself — resolvable (ScheduleEmpty below does it for rootK), but
            //     share's guard is the enclosing LIST's Count, not a sibling FIELD, which this table has no
            //     shape for; left tooltip-only rather than invented.
            //
            // Every entry a boolean CAN be computed for is registered under "DeclaringType.FieldName", the
            // same key ConditionOf uses, so the reason shown when disabled is exactly the sentence T-0280
            // already wrote — a disabled control and its own tooltip can never disagree.
            static readonly Dictionary<string, Func<object, bool>> LiveIf = new Dictionary<string, Func<object, bool>>
            {
                // JetSettings — shared by Jet, Radial Jet and Explosive Jet. pulseN/pulseDepth and
                // shockN/shockDepth are the mutual pairs above and are deliberately absent from this table.
                { "JetSettings.sweepN",    o => NonZero(o, "sweep") },
                { "JetSettings.ringK",     o => NonZero(o, "ringN") },
                { "JetSettings.ringR0",    o => NonZero(o, "ringN") },
                { "JetSettings.ringGrow",  o => NonZero(o, "ringN") },
                { "JetSettings.ringLife",  o => NonZero(o, "ringN") },
                { "JetSettings.ringReach", o => NonZero(o, "ringN") },
                { "JetSettings.ringAmp",   o => NonZero(o, "ringN") },
                { "JetSettings.rootAmp",   o => NonZero(o, "rootR") },

                // RadialJetSettings — lobes/lobeDepth/lobeKick are the circular triple above, absent here.
                { "RadialJetSettings.rootK",    o => NonZero(o, "srcR") },
                { "RadialJetSettings.ringFlat", o => NonZero(o, "ringN") },

                // ExplosiveJetSettings — lobes/lobeDepth/lobeKick absent (same circular triple); rootK's
                // SECOND condition needs the schedule list, resolvable on this owner (the schedule sits on the
                // same settings box as rootK), so rootK stays live-checked unlike RadialJetSettings' plainer one.
                { "ExplosiveJetSettings.ringFlat", o => NonZero(o, "ringN") },
                { "ExplosiveJetSettings.ringArc",  o => NonZero(o, "ringFlat") && NonZero(o, "ringN") },
                { "ExplosiveJetSettings.rootK",    o => NonZero(o, "srcR") && ScheduleEmpty(o, "schedule") },

                // TorchSettings — every one of these is one-directional (PyreTorch.cs:638,657; none of lash/
                // pulse/pulseGain/bulge/curl is itself gated by the dial it guards).
                { "TorchSettings.lashK",    o => NonZero(o, "lash") },
                { "TorchSettings.lashWave", o => NonZero(o, "lash") },
                { "TorchSettings.lashPh",   o => NonZero(o, "lash") },
                { "TorchSettings.pulseN",   o => NonZero(o, "pulse") || NonZero(o, "pulseGain") || NonZero(o, "bulge") },
                { "TorchSettings.pulsePh",  o => NonZero(o, "pulse") || NonZero(o, "pulseGain") || NonZero(o, "bulge") },
                { "TorchSettings.bulgeW",   o => NonZero(o, "bulge") },
                { "TorchSettings.curlX",    o => NonZero(o, "curl") },
                { "TorchSettings.curlY",    o => NonZero(o, "curl") },

                // ── T-0336 ───────────────────────────────────────────────────────────────────────────────
                // Every one of these is ONE-DIRECTIONAL, which is what makes it safe to disable: the guard is
                // itself live at the form's own defaults and is never gated by the dial it guards, so a greyed
                // dial can always be un-greyed from the control right beside it. The MUTUAL pairs this sweep
                // also found (Drift X / Drift Y ↔ Drift Amount, and Lobe Count ↔ Lobe Amount, both of which
                // ship at 0 and each of which is dead while the other is) are deliberately absent, exactly as
                // JetSettings' pulseN/pulseDepth are — greying both sides would lock the pair shut for good.
                { "PlasmaBloomForm.biasDir",  o => NonZero(o, "biasAmt") },
                { "PlasmaBloomForm.biasK",    o => NonZero(o, "biasAmt") },
                { "PlasmaBloomForm.halfDir",  o => NonZero(o, "halfAmt") },
                { "PlasmaBloomForm.halfSoft", o => NonZero(o, "halfAmt") },
                { "PlasmaBloomForm.halfK",    o => NonZero(o, "halfAmt") },
                { "PlasmaBloomForm.lobeMode", o => NonZero(o, "lobeAmp") },
                { "PlasmaBloomForm.lobePow",  o => NonZero(o, "lobeAmp") },
                { "PlasmaBloomForm.lobePh",   o => NonZero(o, "lobeAmp") },

                { "ForkBlastForm.aim",       o => Below(o, "spread", 179.9f) },
                { "ForkBlastForm.gobSizePx", o => NonZero(o, "gobs") },
                { "ForkBlastForm.gobReach",  o => NonZero(o, "gobs") },
                { "ForkBlastForm.gobSwell",  o => NonZero(o, "gobs") },
                { "ForkBlastForm.gobLife",   o => NonZero(o, "gobs") },
                { "ForkBlastForm.gobAmount", o => NonZero(o, "gobs") },
                { "ForkBlastForm.gobTiming", o => NonZero(o, "gobs") },

                // ── T-0337 ───────────────────────────────────────────────────────────────────────────────
                // Thirteen of round 20's twenty. Seven of that twenty are deliberately absent from this
                // table and carry a sentence only: ArcBurstForm.keepHueFloor (content-dependent, not gated),
                // LatticeSettings.flyLo/flyHi and CageSettings.coolK (live, but only late in the clock — and
                // for coolK, whether ANY frame lands in its window depends on the document's frame count,
                // which is not a field on the owner and must not decide a greyed control), ForkBlastForm.flash
                // (brief, not gated) and ForkBlastForm.opacity (its condition is the Fill drawn ABOVE these
                // dials, not a sibling field — and the Fill row does not rebuild the card, so greying on it
                // would leave a dial stuck grey after the author had already fixed it).
                { "ArcBurstForm.ghostDeepLo", o => LayoutDeepGhost(o) },
                { "ArcBurstForm.ghostDeepHi", o => LayoutDeepGhost(o) },

                { "PlasmaBloomForm.driftEase", o => DriftOpen(o) },
                { "PlasmaBloomForm.driftLin",  o => DriftOpen(o) },
                { "PlasmaBloomForm.driftLag",  o => DriftOpen(o) },

                { "PlasmaBloomForm.mode",       o => AtLeast(o, "lobes", 2f) && (NonZero(o, "lobeAmp") || NonZero(o, "plumeAmp")) },
                { "PlasmaBloomForm.gateGain",   o => AtLeast(o, "lobes", 2f) && (NonZero(o, "lobeAmp") || NonZero(o, "plumeAmp")) },
                { "PlasmaBloomForm.plumeAmp",   o => AtLeast(o, "lobes", 2f) },
                { "PlasmaBloomForm.plumeReach", o => AtLeast(o, "lobes", 2f) && NonZero(o, "plumeAmp") },
                { "PlasmaBloomForm.plumeW",     o => AtLeast(o, "lobes", 2f) && NonZero(o, "plumeAmp") },
                { "PlasmaBloomForm.plumeVary",  o => AtLeast(o, "lobes", 2f) && NonZero(o, "plumeAmp") && NonZero(o, "warp") },

                // the only guard on this list that does not live on the dial's own owner
                { "PlasmaPopulation.swirl", o => RootNonZero("swirl") },
            };

            // The field names LiveIf's checks read — editing ANY of these has to rebuild the card (T-0281),
            // the same way a [ZUIShowIf] gate already does, or a dial that just became reachable stays greyed
            // until something unrelated happens to rebuild it. Keyed the same way (DeclaringType.FieldName) so
            // an inherited guard (ringN lives on JetSettings even when read through a RadialJetSettings/
            // ExplosiveJetSettings instance) is named once regardless of which subtype owns the instance.
            static readonly HashSet<string> InertGuardFields = new HashSet<string>
            {
                "JetSettings.sweep", "JetSettings.ringN", "JetSettings.rootR",
                "RadialJetSettings.srcR",
                "ExplosiveJetSettings.srcR", "ExplosiveJetSettings.ringFlat", "ExplosiveJetSettings.schedule",
                "TorchSettings.lash", "TorchSettings.pulse", "TorchSettings.pulseGain", "TorchSettings.bulge",
                "TorchSettings.curl",
                // T-0336
                "PlasmaBloomForm.biasAmt", "PlasmaBloomForm.halfAmt", "PlasmaBloomForm.lobeAmp",
                "ForkBlastForm.gobs", "ForkBlastForm.spread",
                // T-0337 — `layout` is already a [ZUIShowIf] gate and rebuilds on its own, but each pattern's
                // own Deep ghost share is not, and neither is anything in the drift/lobe/plume cluster.
                "CoreSettings.ghostDeepP", "WeaveSettings.ghostDeepP", "BoltSettings.ghostDeepP",
                "TerminalSettings.ghostDeepP", "CageSettings.ghostDeepP", "StippleSettings.ghostDeepP",
                "PinchSettings.ghostDeepP", "LichtenSettings.ghostDeepP",
                "PlasmaBloomForm.driftX", "PlasmaBloomForm.driftY", "PlasmaBloomForm.driftAmt",
                "PlasmaBloomForm.lobes", "PlasmaBloomForm.plumeAmp", "PlasmaBloomForm.warp",
                "PlasmaBloomForm.swirl",
            };

            static bool IsDialInertGuard(FieldInfo f)
                => f.DeclaringType != null && InertGuardFields.Contains(f.DeclaringType.Name + "." + f.Name);

            static string DialInertReason(FieldInfo f, object owner)
            {
                if (f.DeclaringType == null) return null;
                string key = f.DeclaringType.Name + "." + f.Name;
                if (!LiveIf.TryGetValue(key, out var isLive)) return null;   // not a live-checkable guard — tooltip only
                if (isLive(owner)) return null;                             // guard open — the dial can act
                return ConditionOf.TryGetValue(key, out var why) ? why : "Nothing until its guard dial is above 0.";
            }

            /// Reads sibling field `name` on `owner` (walking the owner's OWN inheritance chain via ZuiReflect's
            /// own field cache, so an inherited guard like JetSettings.ringN is found through a
            /// RadialJetSettings/ExplosiveJetSettings instance too) and asks whether it is "on": non-zero for a
            /// number, true for a bool, non-zero staticValue for a ZUIValue. An unknown name fails OPEN (treated
            /// as live) — the same "a stale reference shows the control rather than silently hiding it" posture
            /// VisibleNow takes for [ZUIShowIf].
            static bool NonZero(object owner, string name)
            {
                var f = FindSibling(owner, name);
                if (f == null) return true;
                object v = f.GetValue(owner);
                switch (v)
                {
                    case ZUIValue zv: return zv.staticValue != 0f;
                    case float fl: return fl != 0f;
                    case int i: return i != 0;
                    case bool b: return b;
                    default: return true;
                }
            }

            /// T-0336 — the same read as <see cref="NonZero"/>, for a guard whose "open" state is a value BELOW
            /// a limit rather than above zero: Fork Blast drops the aim direction outright at a full-circle
            /// spread (<c>PyreForkBlast.cs:215</c>). Fails OPEN on an unknown name, exactly as NonZero does.
            static bool Below(object owner, string name, float limit)
            {
                var f = FindSibling(owner, name);
                if (f == null) return true;
                object v = f.GetValue(owner);
                switch (v)
                {
                    case ZUIValue zv: return zv.staticValue < limit;
                    case float fl: return fl < limit;
                    case int i: return i < limit;
                    default: return true;
                }
            }

            /// T-0337 — the mirror of <see cref="Below"/>: a guard whose "open" state is a value AT OR ABOVE a
            /// limit rather than merely non-zero. Plasma Bloom's lobe gate is not computed at all below two
            /// lobes (<c>PyrePlasmaBloom.cs:327</c>, <c>bool lobes = f.lobes &gt;= 2;</c>), so "non-zero" would
            /// wrongly free every dial behind it at a single lobe. Fails OPEN on an unknown name.
            static bool AtLeast(object owner, string name, float limit)
            {
                var f = FindSibling(owner, name);
                if (f == null) return true;
                object v = f.GetValue(owner);
                switch (v)
                {
                    case ZUIValue zv: return zv.staticValue >= limit;
                    case float fl: return fl >= limit;
                    case int i: return i >= limit;
                    default: return true;
                }
            }

            /// T-0337 — Plasma Bloom's drift trio (Ease / Linear / Lag). `DriftOf` returns early while Drift X
            /// and Drift Y are BOTH zero (<c>PyrePlasmaBloom.cs:206</c>) and multiplies by Drift Amount on the
            /// next line, so the three dials downstream of it need both halves open. The two halves are each
            /// other's mutual pair and are deliberately left live themselves (see the note above), which is
            /// what stops this from locking anything shut: a greyed Ease is always one drag away from live.
            static bool DriftOpen(object owner)
                => (NonZero(owner, "driftX") || NonZero(owner, "driftY")) && NonZero(owner, "driftAmt");

            /// T-0337 — Arc Burst's deep-ghost pair is declared on the FORM, but the share that gates it lives
            /// on whichever pattern's settings box is currently in use (<c>PyreArcBurst.cs:403</c>). Crown and
            /// Lattice never call Ghost at all and carry no such field, so an absent one reads as shut. Fails
            /// OPEN if the layout field or its settings object cannot be resolved.
            static readonly Dictionary<string, string> ArcLayoutSettings = new Dictionary<string, string>
            {
                { "Core", "core" }, { "Weave", "weave" }, { "Bolt", "bolt" }, { "Terminal", "terminal" },
                { "Cage", "cage" }, { "Stipple", "stipple" }, { "Pinch", "pinch" }, { "Lichten", "lichten" },
            };

            static bool LayoutDeepGhost(object owner)
            {
                var layoutField = FindSibling(owner, "layout");
                if (layoutField == null) return true;
                string layout = layoutField.GetValue(owner)?.ToString();
                if (layout == null) return true;
                if (!ArcLayoutSettings.TryGetValue(layout, out var settingsName)) return false;  // Crown / Lattice
                var settingsField = FindSibling(owner, settingsName);
                object settings = settingsField?.GetValue(owner);
                return settings == null || NonZero(settings, "ghostDeepP");
            }

            /// T-0337 — the one guard on this card that does not sit on the dial's own owner: a population's
            /// "Swirl follow" is multiplied by the FORM's own Swirl (<c>PyrePlasmaBloom.cs:245</c>), and the
            /// population object has no way back to the form. The drawer therefore names the form it is
            /// currently reflecting for the duration of the FlowFields call, which is synchronous — the whole
            /// card is built inside <see cref="Drawer.Build"/> before anything else can run. Unset means no
            /// card is being built, and like every other read here that fails OPEN.
            [ThreadStatic] static object _reflectingForm;

            static bool RootNonZero(string name)
                => _reflectingForm == null || NonZero(_reflectingForm, name);

            /// ExplosiveJetSettings.rootK's second condition — the Blast schedule (a List<ExplosiveBlast> on the
            /// SAME settings box) holding no detonations. Empty/null/missing all read as "empty".
            static bool ScheduleEmpty(object owner, string name)
            {
                var f = FindSibling(owner, name);
                return f?.GetValue(owner) is not IList list || list.Count == 0;
            }

            static FieldInfo FindSibling(object owner, string name)
            {
                foreach (var f in ZuiReflect.FieldsOf(owner.GetType()))
                    if (f.Name == name) return f;
                return null;
            }

            // ── ramp knobs Jet/Orb's bake ignores (T-0281) ────────────────────────────────────────────────
            // JetShade.Bake (PyreJetEngine.cs) and PyreOrb.BakeLut (PyreOrb.cs) each fold a ramp's Adjust
            // knobs into the baked table (their own ApplyAdjust) — reverse/phase/quantiseSteps/hueShift/
            // saturation/brightness/contrast all reach the picture — but BOTH interpolate the stops directly
            // in linear light and never branch on `space`: measured T-0280, flipping it changes 0 of 1024 LUT
            // entries in JetShade.Bake and 0 of 768 in PyreOrb.BakeLut. `cycle` is correctly never read at
            // bake time either — it is an instruction to a runtime driver, not a still-frame knob
            // (ZuiRampAdjust's own doc); `cycleSpeed` has no control on ZuiRampControl at all, so there is
            // nothing to grey for it here.
            //
            // Plasma Bloom's hueA/hueB are NOT listed: PlasmaBloomForm samples them with EvalStops/Evaluate
            // directly (PlasmaBloomForm.cs:419, PyreShade.EvalStops), never through a baked LUT, so every knob
            // including the blend mode reaches the picture there and stays fully live.
            static readonly string[] BakedRampHonoured =
                { "reverse", "phase", "quantiseSteps", "hueShift", "saturation", "brightness", "contrast" };

            static readonly HashSet<string> BakedRampFields = new HashSet<string>
            {
                "JetSettings.ramp", "JetSettings.sootRamp",   // shared by Jet, Radial Jet, Explosive Jet
                "StyleSettings.ramp",                          // Orb — Emberdrift/Wisp/Coronal/Membrane/Voltcore
            };

            static string[] RampHonouredKnobs(FieldInfo f, object owner)
                => f.DeclaringType != null && BakedRampFields.Contains(f.DeclaringType.Name + "." + f.Name)
                    ? BakedRampHonoured : null;
        }
    }
}
