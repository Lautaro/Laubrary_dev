// ChunkWindow.FragmentFractureCard — the card for a Fragment Fracture: the big-piece cut producer. Shape
// copied from PyreBlastCard: the source pickers are wide by nature and get their own rows first, the one
// control that changes what else is shown (Inherit burst direction) sits directly above the field it hides,
// the rest pack into rows of short controls, and the layer slot is last.
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        void BuildFragmentFractureCard(VisualElement body, ChunkSpec c, FragmentFracture cap)
        {
            string id = cap.id;

            // ── what gets cut ─────────────────────────────────────────────────────
            body.Add(Z.Field("Source",
                "Animated content to fracture — a Zoe, a Pyre, anything that can hand over frames. Its FIRST " +
                "frame is the picture that gets cut, so a character comes apart in the pose it was in. Outranks " +
                "the plain sprite below. When a Zoe triggers this burst, its live current sprite outranks both " +
                "this and the fallback sprite below — this is what a standalone burst uses instead.",
                AssetPicker(cap.sourceVisual,
                            o => DialAndRebuildCard(id, "Set Fracture Source", () => cap.sourceVisual = o),
                            typeof(IChunkAnimation), "Source",
                            "Animated content to fracture. Its first frame is the picture that gets cut.")));

            // A plain Sprite, not a LauAsset — Z.Object<T>, matching every other raw-Sprite field in the
            // codebase (Pyre, BackSplash, Cartographer, SpriteFx); AssetPicker's LauAsset chip only browses
            // registered LauAsset types and would show nothing for a plain imported sprite.
            body.Add(Z.Field("Fallback sprite",
                "The plain sprite that gets cut when Source above is empty. Its texture needs Read/Write " +
                "Enabled. When a Zoe triggers this burst, its live current sprite outranks this too — this is " +
                "what a standalone burst uses instead.",
                Z.Object<Sprite>(cap.source, "The plain sprite that gets cut when Source above is empty.",
                    v => Dial("Set Fracture Sprite", () => cap.source = v), 200f)));

            // Says so when nothing will be cut (no source, unreadable pixels); absent otherwise.
            body.Add(SourceStateLine(c, cap));

            // The bounds are the cutter's own (it clamps to exactly this band), so the dial cannot offer a
            // number the cut then silently rewrites — the old field accepted 1 and 40 alike and quietly got 2
            // and 12.
            body.Add(Z.HGroup(
                Z.MicroSlider("Pieces", cap.pieceCount, FragmentCutter.MinPieces, FragmentCutter.MaxPieces,
                    "How many pieces the picture is cut into. 2–6 keeps each piece recognisable as part of it.",
                    v => Dial("Edit Piece Count", () => cap.pieceCount = Mathf.RoundToInt(v)),
                    150f, showValue: true, decimals: 0),
                Z.Field("Min area",
                    "Smallest piece, in source pixels. Anything below this merges into its neighbour instead " +
                    "of becoming a fragment nobody can see.",
                    Z.Int(cap.minPieceAreaPx, "Smallest piece, in source pixels.",
                        v => Dial("Edit Min Piece Area", () => cap.minPieceAreaPx = Mathf.Max(1, v)), 70f)),
                SeedField(cap.seed, "the cut and the pieces' flight", "Edit Fracture Seed", v => cap.seed = v)));

            // Same Auto-pattern as Debris Scatter (T-0383): the cut pieces' own pixels-per-unit defaults to
            // the project's Pixel Scale setting rather than the source sprite's own baked import value, which
            // can drift from it (T-0390 — that drift was rendering fragments sub-pixel tiny).
            body.Add(Z.Toggle("Use project pixel scale",
                "Pull the cut pieces' pixels-per-unit from the project's Pixel Scale Project Settings asset " +
                "instead of the override below. Off = always use the override, whatever the project says.",
                cap.useProjectPixelScale, v => DialAndRebuildCard(id, "Toggle Use Project Pixel Scale",
                    () => cap.useProjectPixelScale = v)));
            if (!cap.useProjectPixelScale)
                body.Add(Z.Field("Pixels/unit",
                    "Pixels-per-unit for the cut pieces. Affects how crisp/blocky they are, not their size on " +
                    "screen — a piece is always exactly the size it was in the source picture.",
                    Z.Float(cap.pixelsPerUnitOverride, "Pixels-per-unit for the cut pieces.",
                        v => Dial("Edit Pixels/Unit", () => cap.pixelsPerUnitOverride = Mathf.Max(1f, v)), 70f)));
            else
                body.Add(Z.Text($"Currently {cap.EffectivePixelsPerUnit:0.#} px/unit (project setting).",
                    ZuiText.Subtle, "The live value read from the project's Pixel Scale Project Settings asset."));

            // ── how it flies ──────────────────────────────────────────────────────
            body.Add(Z.HGroup(
                Z.MicroMinMax("Speed", cap.speedMin, cap.speedMax, 0f, 20f,
                    "Launch speed, world units/sec. Each piece picks one random speed in between.",
                    (lo, hi) => Dial("Edit Speed", () => { cap.speedMin = lo; cap.speedMax = hi; }),
                    150f, showValue: true, decimals: 2),
                Z.Toggle("Inherit burst direction",
                    "Aim the pieces along the recipe's own direction instead of the angle set below.",
                    cap.useBurstDirection, v => DialAndRebuildCard(id, "Toggle Inherit Direction", () => cap.useBurstDirection = v))));

            if (!cap.useBurstDirection)
                body.Add(Z.MicroSlider("Direction", cap.directionDeg, 0f, 360f,
                    "Centre of the cone in degrees. 0 = right, 90 = up.",
                    v => Dial("Edit Direction", () => cap.directionDeg = v), 150f, showValue: true, decimals: 0));

            body.Add(Z.MicroSlider("Spread", cap.spreadDeg, 0f, 180f,
                "Cone half-angle. 180 = each piece flies straight out from where it sat; 0 = all the same way.",
                v => Dial("Edit Spread", () => cap.spreadDeg = v), 150f, showValue: true, decimals: 0));

            body.Add(Z.HGroup(
                Z.MicroSlider("Gravity", cap.gravity, 0f, 40f,
                    "Downward acceleration, world units/sec².",
                    v => Dial("Edit Gravity", () => cap.gravity = v), 150f, showValue: true, decimals: 1),
                Z.MicroSlider("Drag", cap.drag, 0f, 5f,
                    "Air resistance: per-second damping of velocity.",
                    v => Dial("Edit Drag", () => cap.drag = v), 150f, showValue: true, decimals: 2)));

            body.Add(Z.MicroMinMax("Spin", cap.angularSpeedMin, cap.angularSpeedMax, 0f, 720f,
                "Spin rate, degrees/sec. Each piece's direction is randomised.",
                (lo, hi) => Dial("Edit Spin", () => { cap.angularSpeedMin = lo; cap.angularSpeedMax = hi; }),
                150f, showValue: true, decimals: 0));

            // ── life ──────────────────────────────────────────────────────────────
            body.Add(Z.MicroMinMax("Life", cap.lifeMin, cap.lifeMax, 0.01f, 8f,
                "Lifetime, seconds.",
                (lo, hi) => Dial("Edit Life", () => { cap.lifeMin = Mathf.Max(0.01f, lo); cap.lifeMax = Mathf.Max(cap.lifeMin, hi); }),
                150f, showValue: true, decimals: 2));

            body.Add(Z.Field("Alpha over life",
                "Opacity across a piece's life, left (spawn) to right (death).",
                Z.Envelope(cap.alphaEnvelope,
                    new ZuiEnvelopeOptions { xMin = 0f, xMax = 1f, yMin = 0f, yMax = 1f },
                    "Opacity across a piece's life, left (spawn) to right (death).",
                    onChanged: EnvelopeChanged,
                    onBeforeMutate: () => EnvelopeUndo("Edit Alpha Over Life"))));

            var slot = LayerSlotRow(c, () => cap.layerName, v => cap.layerName = v);
            if (slot != null) body.Add(slot);
        }
    }
}
