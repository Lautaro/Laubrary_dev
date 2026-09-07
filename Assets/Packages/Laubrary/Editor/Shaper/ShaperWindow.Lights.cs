// ShaperWindow.Lights — the DOCUMENT light rig UI (W2.2, T-0164).
//
// The design's central idea is "shine belongs to the lights, not to the paint" (LR-1.1/LR-1.3), yet before
// this file ShaperDocument.lightRig (Runtime/Shaper/ShaperLightRig.cs:100-136) had no window at all — every
// ShaperLight (ShaperLightRig.cs:8-136) was Inspector-only. This is a single DOCUMENT-level card, built
// beside Canvas rather than under a layer/node, because the rig is not owned by any layer (LR-1.1's own
// comment: "No light is owned by a node, a layer, a fill, a border or a generator").
//
// Card layout follows ui-layout-rules.md's repeating-card shape: the header carries identity plus the one
// control every card has (here: enable, kind, remove — ZuiBox's header only offers a single right-aligned
// slot via AddHeaderContent, so all three land there rather than split left/right the way a plain-row card
// like BuildLayerRow does); the body's short fields pack into HGroups.
//
// Inert dials are DECLARED, not hidden, matching the Solids pattern this file's own SolidVal
// (ShaperWindow.Sections.cs:139-150) established: Directional has no position/range (ShaperLightLaw.cs:227-
// 234 branches entirely on kind, and a directional light's posX/Y/Z and range are never read), and yaw/pitch
// only steer a Directional light's fixed direction (ShaperLight.cs:45-55) — a Point light's direction is
// implied by its position each sample (ShaperLightLaw.cs:237-240), so aiming it has nothing to do.
//
// No preview overlay (authoring.md §15) — SKIPPED, not built. A light's position/direction marker would
// need its own capability interface in Runtime/Shaper and a collector in Editor/Shaper, which is a second
// well-scoped task; adding it here as an afterthought risks exactly the hardcoded-toggle anti-pattern §15
// exists to prevent.
using System;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Shaper.Editor
{
    public partial class ShaperWindow
    {
        ZuiSection lightsSection;
        VisualElement lightListHost;

        /// The document's Lights card: the rig's one ambient term, then every ShaperLight in authored order.
        /// Called by the shell (ShaperWindow.cs BuildAsset) beside BuildCanvasSection.
        internal void BuildLightsSection(VisualElement root)
        {
            var rig = document.lightRig;
            if (rig == null) return;

            var box = lightsSection = Z.Section("Lights",
                LightsSectionTooltip(rig), "shaper.window.lights", icon: "sun");

            box.Add(Z.HGroup(
                Z.Field("Ambient", "The rig's one ambient term (LR-1.3), added once before any light and never "
                    + "scaled by a layer's Intensity ×. Per-light ambient would let switching one lamp off "
                    + "darken shadows that lamp was never pointed at — this document has exactly one.",
                    Z.Color(rig.ambientColour, "The ambient's colour.",
                        c => Change(() => rig.ambientColour = c), 90f)),
                Val("Ambient ×", "The ambient's strength. Default 0.18 reproduces Pyre's own ReliefLight "
                    + "ambient default (ShaperLightRig.cs:121-131).", rig.ambientIntensity, 0f, 2f)));

            lightListHost = new VisualElement();
            box.Add(lightListHost);
            RebuildLightList();

            bool atCap = rig.lights.Count >= ShaperLightRig.MaxLights;
            var addBtn = Z.Button("+ Add light", atCap
                    ? $"The rig is already at its cap of {ShaperLightRig.MaxLights} lights (LR-1.4) — remove "
                      + "one before adding another."
                    : "Add a new light to the rig, lighting every layer in the document.",
                () =>
                {
                    Change(() => rig.lights.Add(new ShaperLight { name = "Light " + (rig.lights.Count + 1) }));
                    RebuildLightList();
                });
            addBtn.SetEnabled(!atCap);
            box.Add(addBtn);

            root.Add(box);
        }

        void RebuildLightList()
        {
            if (lightListHost == null) return;
            lightListHost.Clear();
            var rig = document.lightRig;
            if (rig == null) return;
            for (int i = 0; i < rig.lights.Count; i++)
                lightListHost.Add(BuildLightCard(lightListHost, i));
            // T-0200 — every add/remove/enable-toggle of a light routes through here (BuildLightsSection calls
            // it once up front, the +Add button and every card's enable checkbox and remove button call it
            // again), so this is the ONE place that needs to re-check "does the rig still have zero enabled
            // lights" rather than a refresh wired into each of those three call sites separately.
            lightsSection?.SetTooltip(LightsSectionTooltip(rig));
        }

        /// T-0200 — the Lights section's header tooltip, honest about whether the rig is currently doing
        /// anything: <see cref="ShaperLightRig.NoLightsRenderUnlit"/> while it holds zero ENABLED lights (the
        /// exact condition <see cref="ShaperLightCompiler.CompileResponse"/> gates every layer's shading on),
        /// the ordinary description otherwise.
        static string LightsSectionTooltip(ShaperLightRig rig)
        {
            bool anyEnabled = false;
            if (rig?.lights != null)
                for (int i = 0; i < rig.lights.Count; i++)
                    if (rig.lights[i] != null && rig.lights[i].enabled) { anyEnabled = true; break; }
            if (!anyEnabled) return ShaperLightRig.NoLightsRenderUnlit;
            return "The document's one light rig (LR-1.1): a single ambient term plus an ordered list of lights. "
                + "No light is owned by a layer or a fill — every layer is lit by this same rig, which is what "
                + "keeps a multi-layer picture reading as one scene rather than several independently-lit ones.";
        }

        VisualElement BuildLightCard(VisualElement listHost, int index)
        {
            var rig = document.lightRig;
            var light = rig.lights[index];
            int li = index;

            // Keyed by ordinal rather than a stable per-light id (ShaperLight carries none): fold state can
            // drift onto a different light across a reorder, the same trade-off ui-layout-rules.md accepts
            // for BoxKeyed elsewhere in this tool. Low cost here — at most 8 lights, and drift only affects
            // which cards are folded, never any authored value.
            var card = Z.BoxKeyed(string.IsNullOrEmpty(light.name) ? "Light" : light.name,
                "One authored light (LR-1.2). No light is owned by a layer — this same rig lights every layer "
                + "in the document.", "shaper.window.light." + li);

            var grip = Z.Text("≡", ZuiText.Body, "Drag to reorder this light. Rig order is authored (LR-2.3) — "
                + "lights shade in this order, and floating-point summation is not associative, so reordering "
                + "can change the result.");
            grip.style.unityFontStyleAndWeight = FontStyle.Bold;
            grip.style.width = 16f;
            ZuiReorder.MakeGrip(grip, card, listHost, (from, to) =>
            {
                Change(() =>
                {
                    var l = rig.lights[from];
                    rig.lights.RemoveAt(from);
                    rig.lights.Insert(to, l);
                });
                RebuildLightList();
            });
            card.AddHeaderContent(grip);

            card.AddHeaderContent(Z.Toggle("", "Show or hide this light. A disabled light contributes nothing "
                + "to the rig.", light.enabled, v => { Change(() => light.enabled = v); RebuildLightList(); }));

            card.AddHeaderContent(Z.Segmented((int)light.kind, ShaperWords.Names(typeof(ShaperLightKind)),
                "Directional has no position — it shades from a fixed direction with no falloff (LR-2.4). "
                + "Point has a position and falls off with range.",
                v => { Change(() => light.kind = (ShaperLightKind)v); RebuildLightList(); }));

            card.AddHeaderContent(Z.Button("×", "Remove this light.", () =>
            {
                Change(() => rig.lights.RemoveAt(li));
                RebuildLightList();
            }).W(22f));

            card.Add(Z.Field("Name", "This light's name.",
                Z.TextInput(light.name ?? "", "This light's name.",
                    v => { Change(() => light.name = v); RebuildLightList(); }, 150f)));

            card.Add(Z.HGroup(
                Z.Field("Colour", "This light's colour, authored in sRGB and decoded once at compile. There is "
                    + "no second specular colour on a light — the highlight's tint lives on the receiving "
                    + "layer's Spec tint instead (LR-4.4, ShaperLightRig.cs:26-36).",
                    Z.Color(light.colour, "This light's colour.",
                        c => Change(() => light.colour = c), 90f)),
                Val("Intensity", "Multiplies this light's contribution.", light.intensity, 0f, 4f),
                Val("Specular", "This light's own Blinn-Phong strength. Applies to either kind.",
                    light.specular, 0f, 1f)));

            bool isDirectional = light.kind == ShaperLightKind.Directional;
            string dirReason = "Yaw and pitch only steer a Directional light's fixed direction. This light is "
                + "Point, whose direction is implied by its position each sample rather than an authored aim.";
            string posReason = "Position and range only apply to a Point light, which falls off with distance "
                + "(LR-2.4). This light is Directional, which has no location — only a direction it comes from.";

            // Direction/Position are spatial X/Y pairs of animatable ZUIValues (ShaperLightRig.cs:46,55,58,60),
            // so they take Val2D — the SAME shared helper and control T-0168 (commit cc02e986) promoted the
            // Transform section's Translate/Origin/Scale/Skew to (ShaperWindow.cs:937-947): the standard
            // collapsed Value2D row (label + thumbnail + right-click mode menu), not this file's own control.
            // T-0168 also retired the plain Z.Pad this card used between the two PM vet passes — Transform's
            // pairs are envelope-driven now too, so Pad's static-only edit was never the settled shape, only
            // an intermediate one. Reusing ShaperWindow.cs's Val2D (rather than redeclaring a second one, as
            // this file briefly did) keeps Undo/dirty/frame-cache invalidation identical to every other 2D
            // dial in the tool, including the one this exact helper now shares with Transform.
            var dirRow = Val2D("Direction", isDirectional
                    ? "Where this Directional light comes FROM, in the canvas frame — yaw (X) then pitch (Y), "
                      + "degrees. Animate either axis to sweep the light over the document's frames."
                    : dirReason,
                light.yaw, light.pitch,
                // T-0186 — dropped the 110px WithPlotSize override so this matches Pyre's default 140px plot
                // (ZuiValue2DControl.Options.plotSize; Pyre's own Val2D never overrides it either).
                new ZuiValue2DControl.Options().WithRange(0f, 360f, -90f, 90f)
                    .WithAxisLabels("Yaw", "Pitch").WithPrefKey("shaper.light.direction." + li));
            if (!isDirectional) dirRow.SetEnabled(false);

            var posRow = Val2D("Position", isDirectional
                    ? posReason
                    : "This Point light's absolute canvas position, X and Y, canvas pixels. Animate either axis "
                      + "to move the light over the document's frames.",
                light.posX, light.posY,
                new ZuiValue2DControl.Options().WithRange(-256f, 256f, -256f, 256f)
                    .WithPrefKey("shaper.light.position." + li));
            if (isDirectional) posRow.SetEnabled(false);

            card.Add(Z.HGroup(dirRow, posRow));

            var posZ = Val("Position Z", isDirectional ? posReason
                    : "This Point light's Z position, canvas pixels, +Z toward the viewer.",
                light.posZ, -256f, 256f);
            var range = Val("Range", isDirectional ? posReason
                    : "The distance at which this Point light's attenuation reaches 1/2 (LR-2.4). There is no "
                      + "separate falloff-exponent dial — a harder falloff is a second light at a shorter range.",
                light.range, 1f, 512f);
            if (isDirectional) { posZ.SetEnabled(false); range.SetEnabled(false); }
            card.Add(Z.HGroup(posZ, range));

            return card;
        }
    }
}
