// ShaperMockSolids — T-0138 #1, the last of the 18 audit todos. Solids is a REAL, already-built generator
// (Runtime/Shaper/ShaperSolids.cs, D:\UNITY\Laubrary Dev - Shaper) — pseudo-3D Box/Pyramid/Can/Orb/Gem/Ring
// shapes rendered via backface-culled facet geometry, publishing coverage + a surface normal into the SAME
// fill/light pipeline every other Shaper generator uses. Its own doc comment is explicit: "a GENERATOR that
// publishes coverage and a surface normal, going through the ordinary fill and light pipeline like everything
// else — not a composite." T-0127 already wired a HeightField fill's height into the analytic normal for
// real relief-shading.
//
// This file mirrors two real things from ShaperSolids.cs field-for-field and behaviour-for-behaviour:
//   1. ShaperSolidDef's authored fields (:32-82) — every scalar the real class uses ZUIValue for, this mock
//      uses ZUIValue for too, same posture as Border width / Light rig dials / Extrusion elsewhere in this file.
//   2. ShaperSolids.InertReason(form, dial) (:312-368) — the declared-inertness table: which dial does
//      nothing on which form, and why, so a future window can grey the control and show the reason without
//      re-deriving any of this. The mock's own InertReason below carries the SAME (form, dial) → reason-or-null
//      shape, paraphrased into plain sentences (the real one cites PyreRenderer.cs line numbers that mean
//      nothing outside that file).
//
// ── The one thing this file does NOT settle, and must not pretend to ────────────────────────────────────
// The real engine's ShaperNodeKind (Runtime/Shaper/ShaperNode.cs) has EXACTLY 3 values — Primitive, Bag,
// Composite. There is no 4th "Solid" kind today, and ShaperSolidDef is referenced nowhere on ShaperNode
// itself; ShaperFillResolver.PaintTile reads a compiled ShaperSolidGeometry from a separate scene.solid[owner]
// slot instead. In other words: HOW a document actually authors a Solid — a 4th node kind, a Composite-style
// alternate source, a separate document/layer-level slot (the way the Light Rig is document-level rather than
// node-level), or something else — is a genuine, UNRESOLVED engine-integration decision, not a UI detail.
// Adding ShaperMockNodeKind.Solid in ShaperMockData.cs is this mock's own placement CHOICE, made so the
// authoring UI below has something concrete to attach to — it is not a claim about the real answer. See the
// Z.Help box BuildSolidBody adds when Solid is selected, which says this in plain language to anyone using
// the mock, not just a future code-reader of this comment.
using System;
using UnityEngine;

namespace ShaperMock.Editor
{
    /// Mirrors the real ShaperSolidForm (ShaperSolids.cs:8-16) — APPEND-ONLY, same as every other mirrored
    /// enum in this mock.
    public enum ShaperMockSolidForm { Box = 0, Pyramid = 1, Can = 2, Orb = 3, Gem = 4, Ring = 5 }

    /// Mirrors the real ShaperSolidDial (ShaperSolids.cs:88-93) — every authored dial on a Solids generator,
    /// named so InertReason and a UI can talk about the same thing.
    public enum ShaperMockSolidDial
    {
        Size = 0, Centre = 1, Aspect = 2, Depth = 3,
        GemSides = 4, GemCrown = 5, GemPavilion = 6, RingInner = 7,
        Yaw = 8, Tilt = 9, Roll = 10, LineWidth = 11, EdgeGlow = 12, InnerGlow = 13,
    }

    /// Mirrors the real ShaperSolidDef (ShaperSolids.cs:32-82) field-for-field. Every field the real class
    /// makes a ZUIValue is a ZUIValue here too — this mock does not regress envelope-ready fields to plain
    /// floats (the same standard Border width / Light rig / Extrusion already hold to in this file).
    [Serializable]
    public sealed class ShaperMockSolidDef
    {
        public ShaperMockSolidForm form = ShaperMockSolidForm.Box;

        /// The size envelope, in canvas pixels.
        public ZUIValue size = new ZUIValue(20f);

        /// Canvas position of the solid's centre, canvas pixels.
        public ZUIValue centreX = new ZUIValue(0f);
        public ZUIValue centreY = new ZUIValue(0f);

        /// The Y half-extent multiplier.
        public ZUIValue aspect = new ZUIValue(1f);
        /// The Z half-extent multiplier. Unused by Can (circular section).
        public ZUIValue depth = new ZUIValue(1f);

        /// Gem only: girdle sides, clamped 3..8 in the real compiler.
        public ZUIValue gemSides = new ZUIValue(6f);
        /// Gem only: crown height as a fraction of R.
        public ZUIValue gemCrown = new ZUIValue(0.55f);
        /// Gem only: pavilion depth as a fraction of R.
        public ZUIValue gemPavilion = new ZUIValue(0.85f);

        /// Ring only: hole radius as a fraction of R, clamped 0.1..0.92 in the real compiler.
        public ZUIValue ringInner = new ZUIValue(0.55f);

        /// Yaw about Y, degrees.
        public ZUIValue yaw = new ZUIValue(0f);
        /// Tilt about X, degrees.
        public ZUIValue tilt = new ZUIValue(0f);
        /// Roll about Z in model space, applied first.
        public ZUIValue roll = new ZUIValue(0f);

        /// FACET EDGE LINE half-width, canvas pixels — called a facet edge line and never a border, because
        /// it traces interior facet seams, which no border stage can produce (it draws around the silhouette).
        public ZUIValue lineWidth = new ZUIValue(1.1f);
        public Color lineColour = Color.white;

        /// Halo strength, 0..1.
        public ZUIValue edgeGlow = new ZUIValue(0f);
        public Color edgeGlowColour = Color.white;

        /// Inner-glow strength, 0..1.
        public ZUIValue innerGlow = new ZUIValue(0f);
        public Color innerGlowColour = Color.white;
    }

    /// Mirrors the real ShaperSolids.InertReason (ShaperSolids.cs:312-368) exactly in shape: null means the
    /// dial is live on that form, a sentence means it is inert and why. The reasons below are paraphrased
    /// from the real ones (which cite PyreRenderer.cs line numbers that only mean something inside that
    /// file) — the FACTS (which dial is inert on which form) are the same, measured ones the real engine
    /// already ships, not re-derived or guessed here.
    public static class ShaperMockSolids
    {
        public static string InertReason(ShaperMockSolidForm form, ShaperMockSolidDial dial)
        {
            switch (dial)
            {
                case ShaperMockSolidDial.Aspect:
                    if (form == ShaperMockSolidForm.Orb)
                        return "Aspect does nothing on an Orb — a sphere's silhouette is the plain circle "
                             + "d <= R and is never squashed, because a sphere looks identical from every angle.";
                    if (form == ShaperMockSolidForm.Gem)
                        return "Aspect does nothing on a Gem — a gem's proportions are Crown and Pavilion, "
                             + "which set its height above and below the girdle; the reference builder reads "
                             + "neither Aspect nor Depth. Use Crown and Pavilion.";
                    if (form == ShaperMockSolidForm.Ring)
                        return "Aspect does nothing on a Ring — a ring is a flat annulus in one plane, so it "
                             + "has no Y half-extent to multiply; its shape comes from Size, Inner and Tilt.";
                    return null;

                case ShaperMockSolidDial.Depth:
                    if (form == ShaperMockSolidForm.Can)
                        return "Depth does nothing on a Can — its section is circular, so the X and Z "
                             + "half-extents are both Size and there is no separate Z to scale.";
                    if (form == ShaperMockSolidForm.Orb)
                        return "Depth does nothing on an Orb, for the same reason as Aspect: the sphere's "
                             + "silhouette is the plain circle d <= R at every rotation.";
                    if (form == ShaperMockSolidForm.Gem)
                        return "Depth does nothing on a Gem — a gem's proportions are Crown and Pavilion; "
                             + "the reference builder reads neither Aspect nor Depth.";
                    if (form == ShaperMockSolidForm.Ring)
                        return "Depth does nothing on a Ring — a flat annulus has no thickness in its own "
                             + "plane's normal direction.";
                    return null;

                case ShaperMockSolidDial.GemSides:
                case ShaperMockSolidDial.GemCrown:
                case ShaperMockSolidDial.GemPavilion:
                    return form == ShaperMockSolidForm.Gem ? null
                         : "Sides, Crown and Pavilion describe a gem's girdle and its two apexes. They do "
                           + "nothing on a " + form + ".";

                case ShaperMockSolidDial.RingInner:
                    return form == ShaperMockSolidForm.Ring ? null
                         : "Inner is the ring's hole radius. It does nothing on a " + form + ", which has no hole.";

                case ShaperMockSolidDial.Roll:
                    return form == ShaperMockSolidForm.Ring
                         ? "Roll does nothing on a Ring — Roll is a spin about Z in MODEL space, and a ring "
                           + "is rotationally symmetric about its own axis, so rolling it maps the annulus "
                           + "onto itself; the reference builder takes only Yaw and Tilt."
                         : null;

                default:
                    return null;   // Size, Centre, Yaw, Tilt, LineWidth, EdgeGlow and InnerGlow are live on every form.
            }
        }
    }
}
