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
