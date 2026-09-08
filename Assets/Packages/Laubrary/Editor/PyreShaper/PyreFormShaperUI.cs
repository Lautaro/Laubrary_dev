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
        }
    }
}
