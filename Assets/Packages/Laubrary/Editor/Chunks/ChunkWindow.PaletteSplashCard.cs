// ChunkWindow.PaletteSplashCard — the card for a Palette Splash: the "spray of the exploding thing's own
// colours" producer. Shape copied from PyreBlastCard: the source picker is wide by nature and gets its own
// row first, the toggle that changes what else is shown (Inherit burst direction) sits directly above the
// field it hides, the rest pack into rows of short controls, and the layer slot is last.
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        void BuildPaletteSplashCard(VisualElement body, ChunkSpec c, PaletteSplash cap)
        {
            string id = cap.id;

            // ── what gets sampled ─────────────────────────────────────────────────
            // A plain Sprite, not a LauAsset — Z.Object<T>, matching every other raw-Sprite field in the
            // codebase (Pyre, BackSplash, Cartographer, SpriteFx); AssetPicker's LauAsset chip only browses
            // registered LauAsset types and would show nothing for a plain imported sprite.
            body.Add(Z.Field("Sprite",
                "Sprite to sample colours AND the emission footprint from. Empty uses whatever a Fragment " +
                "Fracture in this recipe is cutting.",
                Z.Object<Sprite>(cap.sprite, "Sprite to sample colours and the emission footprint from.",
                    v => Dial("Set Splash Sprite", () => cap.sprite = v), 200f)));

            body.Add(Z.Toggle("From footprint",
                "Spawn each particle from a random opaque pixel of the source, instead of all from one point.",
                cap.emitFromFootprint, v => Dial("Toggle From Footprint", () => cap.emitFromFootprint = v)));

            body.Add(Z.HGroup(
                Z.MicroMinMax("Count", cap.countMin, cap.countMax, 0f, 64f,
                    "How many particles a splash sprays. Each splash picks one random count in between.",
                    (lo, hi) => Dial("Edit Count", () =>
                    {
                        cap.countMin = Mathf.RoundToInt(lo);
                        cap.countMax = Mathf.RoundToInt(hi);
                    }), 150f, showValue: true, decimals: 0),
                Z.MicroMinMax("Size px", cap.sizePxMin, cap.sizePxMax, 0.1f, 20f,
                    "Particle size, in pixels at the Pixels/Unit below. Each particle picks its own in between.",
                    (lo, hi) => Dial("Edit Size Px", () =>
                    {
                        cap.sizePxMin = Mathf.Max(0.1f, lo);
                        cap.sizePxMax = Mathf.Max(cap.sizePxMin, hi);
                    }), 150f, showValue: true, decimals: 1)));

            body.Add(Z.Field("Pixels/unit",
                "Pixels-per-unit the particle sizes above are measured in.",
                Z.Float(cap.pixelsPerUnit, "Pixels-per-unit the particle sizes are measured in.",
                    v => Dial("Edit Pixels/Unit", () => cap.pixelsPerUnit = Mathf.Max(1f, v)), 70f)));

            // ── how it flies ──────────────────────────────────────────────────────
            body.Add(Z.HGroup(
                Z.MicroMinMax("Speed", cap.speedMin, cap.speedMax, 0f, 20f,
                    "Launch speed, world units/sec. Each particle picks one random speed in between.",
                    (lo, hi) => Dial("Edit Speed", () => { cap.speedMin = lo; cap.speedMax = hi; }),
                    150f, showValue: true, decimals: 2),
                Z.Toggle("Inherit burst direction",
                    "Aim the spray along the recipe's own direction instead of the angle set below — what " +
                    "lets a Follow Emitter throw the spray out behind a moving character.",
                    cap.inheritBurstDirection, v => DialAndRebuildCard(id, "Toggle Inherit Direction", () => cap.inheritBurstDirection = v))));

            if (!cap.inheritBurstDirection)
                body.Add(Z.MicroSlider("Direction", cap.directionDeg, 0f, 360f,
                    "Centre of the spray cone in degrees. 0 = right, 90 = up.",
                    v => Dial("Edit Direction", () => cap.directionDeg = v), 150f, showValue: true, decimals: 0));

            body.Add(Z.MicroSlider("Spread", cap.spreadDeg, 0f, 180f,
                "Cone half-angle. 0 = a tight jet; 180 = a full circle.",
                v => Dial("Edit Spread", () => cap.spreadDeg = v), 150f, showValue: true, decimals: 0));

            body.Add(Z.HGroup(
                Z.Field("Gravity",
                    "Downward acceleration, world units/sec².",
                    Z.Float(cap.gravity, "Downward acceleration, world units/sec².",
                        v => Dial("Edit Gravity", () => cap.gravity = Mathf.Max(0f, v)), 70f)),
                Z.MicroSlider("Drag", cap.drag, 0f, 5f,
                    "Air resistance: per-second damping of velocity.",
                    v => Dial("Edit Drag", () => cap.drag = v), 150f, showValue: true, decimals: 2)));

            // ── life ──────────────────────────────────────────────────────────────
            body.Add(Z.HGroup(
                Z.MicroMinMax("Life", cap.lifeMin, cap.lifeMax, 0.02f, 4f,
                    "Particle lifetime, seconds.",
                    (lo, hi) => Dial("Edit Life", () => { cap.lifeMin = Mathf.Max(0.02f, lo); cap.lifeMax = Mathf.Max(cap.lifeMin, hi); }),
                    150f, showValue: true, decimals: 2),
                Z.Field("Seed",
                    "Fixes every random pick so the spray is identical every play. 0 rerolls.",
                    Z.Int(cap.seed, "Fixes every random pick so the spray is identical every play.",
                        v => Dial("Edit Splash Seed", () => cap.seed = v), 70f))));

            body.Add(Z.Field("Alpha over life",
                "Opacity across a particle's life, left (spawn) to right (death).",
                Z.Curve(cap.alphaOverLife, "Opacity across a particle's life, left (spawn) to right (death).",
                    v => Dial("Edit Alpha Over Life", () => cap.alphaOverLife = v))));

            var slot = LayerSlotRow(c, () => cap.layerName, v => cap.layerName = v);
            if (slot != null) body.Add(slot);
        }
    }
}
