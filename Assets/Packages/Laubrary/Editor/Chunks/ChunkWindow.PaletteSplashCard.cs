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
            // Same two-field shape as FragmentFractureCard's Source/Fallback sprite: a reference is always a
            // picker (never a typed name), so a Zoe shows up here by NAME, found the same way Fracture's
            // Source already finds one (T-0363/T-0364 J1 — searching "Floating Disc" used to find nothing
            // because only a raw Sprite field existed).
            body.Add(Z.Field("Source",
                "Animated content to sample colours AND the emission footprint from — a Zoe, a Pyre, anything " +
                "that can hand over frames. Its FIRST frame is what gets sampled. Outranks the plain sprite " +
                "below. When a Zoe triggers this burst, its live current sprite outranks both this and the " +
                "fallback sprite below — this is what a standalone burst uses instead.",
                AssetPicker(cap.sourceVisual,
                            o => DialAndRebuildCard(id, "Set Splash Source", () => cap.sourceVisual = o),
                            typeof(IChunkAnimation), "Source",
                            "Animated content to sample colours and the emission footprint from. Its first " +
                            "frame is what gets sampled.")));

            // A plain Sprite, not a LauAsset — Z.Object<T>, matching every other raw-Sprite field in the
            // codebase (Pyre, BackSplash, Cartographer, SpriteFx); AssetPicker's LauAsset chip only browses
            // registered LauAsset types and would show nothing for a plain imported sprite.
            body.Add(Z.Field("Fallback sprite",
                "The plain sprite to sample colours AND the emission footprint from, when Source above is " +
                "empty and no live sample source is supplied by the caller. Empty uses whatever a Fragment " +
                "Fracture in this recipe is cutting; when a Zoe triggers this burst, its live current sprite " +
                "outranks this too.",
                Z.Object<Sprite>(cap.sprite, "The plain sprite to sample colours and the emission footprint from.",
                    v => Dial("Set Splash Sprite", () => cap.sprite = v), 200f)));

            // Says so when the spray will come out plain white (no sprite anywhere, unreadable pixels).
            body.Add(SourceStateLine(c, cap));

            body.Add(Z.Toggle("From footprint",
                "Spawn each particle from a random opaque pixel of the source, instead of all from one point.",
                cap.emitFromFootprint, v => Dial("Toggle From Footprint", () => cap.emitFromFootprint = v)));

            // ── the hit disc ──────────────────────────────────────────────────────
            // Directly above From footprint's own consequences, because it narrows exactly what that samples.
            BuildHitDiscBlock(body, c, cap, id);

            // ── what a particle is made of ────────────────────────────────────────
            // The ONE control that decides which other controls exist comes first and rebuilds only this card,
            // the same shape Debris Scatter's Visual radio uses — each mode's own dials sit directly under it
            // and are ABSENT, not disabled, when the other mode is picked.
            body.Add(Z.Field("Particles",
                "What each particle is made of. Shards are the tiny procedural pixel shapes tinted with one " +
                "sampled colour. Sampled crops are real little cut-outs of the source art itself, flung with " +
                "all their own detail.",
                Z.MiniRadio((int)cap.particleLook, new[] { "Shards", "Sampled crops" },
                    "What each particle is made of. Only the chosen kind's own dials are authored.",
                    i => DialAndRebuildCard(id, "Set Splash Particles",
                        () => cap.particleLook = (SplashParticleLook)i))));

            body.Add(Z.MicroMinMax("Count", cap.countMin, cap.countMax, 0f, 64f,
                "How many particles a splash sprays. Each splash picks one random count in between.",
                (lo, hi) => Dial("Edit Count", () =>
                {
                    cap.countMin = Mathf.RoundToInt(lo);
                    cap.countMax = Mathf.RoundToInt(hi);
                }), 150f, showValue: true, decimals: 0));

            if (cap.particleLook == SplashParticleLook.SampledCrops)
                BuildSampledCropBlock(body, c, cap, id);
            else
                // Floored at 1, never below: a particle smaller than one game pixel is fine noise, not pixel
                // art, and the runtime clamps it there too — so the slider can't author something Fire will
                // silently override.
                body.Add(Z.MicroMinMax("Size px", cap.sizePxMin, cap.sizePxMax, 1f, 20f,
                    "Particle size, in GAME pixels at the Pixels/Unit below. Each particle picks its own in " +
                    "between. One is the floor: nothing here ever renders smaller than a single game pixel.",
                    (lo, hi) => Dial("Edit Size Px", () =>
                    {
                        cap.sizePxMin = Mathf.Max(1f, lo);
                        cap.sizePxMax = Mathf.Max(cap.sizePxMin, hi);
                    }), 150f, showValue: true, decimals: 1));

            body.Add(Z.Toggle("Use project pixel scale",
                "Pull the particle sizes' pixels-per-unit from the project's Pixel Scale Project Settings asset " +
                "instead of the override below. Off = always use the override, whatever the project says.",
                cap.useProjectPixelScale, v => DialAndRebuildCard(id, "Toggle Use Project Pixel Scale",
                    () => cap.useProjectPixelScale = v)));
            if (!cap.useProjectPixelScale)
                body.Add(Z.Field("Pixels/unit",
                    "Pixels-per-unit the particle sizes above are measured in.",
                    Z.Float(cap.pixelsPerUnitOverride, "Pixels-per-unit the particle sizes are measured in.",
                        v => Dial("Edit Pixels/Unit", () => cap.pixelsPerUnitOverride = Mathf.Max(1f, v)), 70f)));
            else
                body.Add(Z.Text($"Currently {cap.EffectivePixelsPerUnit:0.#} px/unit (project setting).",
                    ZuiText.Subtle, "The live value read from the project's Pixel Scale Project Settings asset."));

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
                Z.MicroSlider("Gravity", cap.gravity, 0f, 40f,
                    "Downward acceleration, world units/sec².",
                    v => Dial("Edit Gravity", () => cap.gravity = v), 150f, showValue: true, decimals: 1),
                Z.MicroSlider("Drag", cap.drag, 0f, 5f,
                    "Air resistance: per-second damping of velocity.",
                    v => Dial("Edit Drag", () => cap.drag = v), 150f, showValue: true, decimals: 2)));

            // ── life ──────────────────────────────────────────────────────────────
            body.Add(Z.HGroup(
                Z.MicroMinMax("Life", cap.lifeMin, cap.lifeMax, 0.02f, 4f,
                    "Particle lifetime, seconds.",
                    (lo, hi) => Dial("Edit Life", () => { cap.lifeMin = Mathf.Max(0.02f, lo); cap.lifeMax = Mathf.Max(cap.lifeMin, hi); }),
                    150f, showValue: true, decimals: 2),
                SeedField(cap.seed, "the spray's random picks", "Edit Splash Seed", v => cap.seed = v)));

            body.Add(Z.Field("Alpha over life",
                "Opacity across a particle's life, left (spawn) to right (death).",
                Z.Envelope(cap.alphaEnvelope,
                    new ZuiEnvelopeOptions { xMin = 0f, xMax = 1f, yMin = 0f, yMax = 1f },
                    "Opacity across a particle's life, left (spawn) to right (death).",
                    onChanged: EnvelopeChanged,
                    onBeforeMutate: () => EnvelopeUndo("Edit Alpha Over Life"))));

            var slot = LayerSlotRow(c, () => cap.layerName, v => cap.layerName = v);
            if (slot != null) body.Add(slot);
        }

        // ── the hit disc: WHICH patch of the art comes loose ─────────────────────────────────────────────────
        // A self-contained on/off feature, so a box with the toggle in its header — the same shape Debris
        // Scatter's Floor uses, not a title that only repeats the first field's name.
        void BuildHitDiscBlock(VisualElement body, ChunkSpec c, PaletteSplash cap, string id)
        {
            var box = Z.BoxKeyed("Hit disc",
                "Take pixels only from a disc on the source art, instead of from anywhere in its opaque " +
                "footprint — the 'the bullet hit HERE and this patch came loose' look.",
                "Chunks.card." + id + ".hitdisc");
            box.AddHeaderContent(Z.Toggle("On",
                "Sample only inside the disc below. Off = the whole opaque footprint, as before this existed.",
                cap.useHitRadius, v => DialAndRebuildCard(id, "Toggle Hit Disc", () => cap.useHitRadius = v)));

            if (cap.useHitRadius)
            {
                box.Add(Z.MicroSlider("Radius px", cap.hitRadiusPx, 0.5f, 64f,
                    "Radius of the disc, in SOURCE-texture pixels of the art being sampled — not game pixels, " +
                    "so it stays the same patch of art whatever the project's Pixel Scale is.",
                    v => DialAndRebuildCard(id, "Edit Hit Radius", () => cap.hitRadiusPx = Mathf.Max(0.5f, v)),
                    150f, showValue: true, decimals: 1));

                box.Add(Z.HGroup(
                    Z.Field("Offset X",
                        "Where the disc sits on the art, in source pixels right of the sprite's pivot.",
                        Z.Float(cap.hitOffsetPx.x, "Source pixels right of the sprite's pivot.",
                            v => DialAndRebuildCard(id, "Edit Hit Offset",
                                () => cap.hitOffsetPx = new Vector2(v, cap.hitOffsetPx.y)), 70f)),
                    Z.Field("Offset Y",
                        "Where the disc sits on the art, in source pixels above the sprite's pivot.",
                        Z.Float(cap.hitOffsetPx.y, "Source pixels above the sprite's pivot.",
                            v => DialAndRebuildCard(id, "Edit Hit Offset",
                                () => cap.hitOffsetPx = new Vector2(cap.hitOffsetPx.x, v)), 70f))));

                box.Add(Z.Text(
                    "The offset picks WHICH patch of art comes loose; the burst itself decides WHERE it flies " +
                    "from, so a spray still bursts out of the real hit point.",
                    ZuiText.Subtle,
                    "This capability lays the source sprite out with its pivot at the burst origin, and nothing " +
                    "tells it where that sprite really sits in the world — so the patch is author-chosen while " +
                    "the launch point stays the live hit position."));

                // Measured, not guessed: the same count Fire filters with, so the card can say out loud that a
                // disc catches nothing BEFORE anyone presses play.
                box.Add(HitDiscCoverageLine(c, cap));
            }
            body.Add(box);
        }

        /// A live "the disc covers N of M opaque pixels" line, including the two failure readings that matter:
        /// a source whose pixels cannot be read at all, and a disc sitting entirely off the art (which falls
        /// back to the whole footprint rather than firing nothing).
        static VisualElement HitDiscCoverageLine(ChunkSpec c, PaletteSplash cap)
        {
            var source = cap.ResolveSprite(c);
            if (source == null)
            {
                var debris = c != null ? c.FirstEnabled<DebrisScatter>() : null;
                source = debris != null ? debris.sampleSource : null;
            }
            if (source == null)
                return Z.Text("No readable source yet — the disc has nothing to sit on.", ZuiText.Subtle,
                    "Set a Source or Fallback sprite above (or a Fragment Fracture / Debris Scatter source in " +
                    "this recipe) and this line measures the disc against it.");

            int inside = cap.CountPixelsInRadius(source, out int total);
            if (inside < 0)
                return Z.Text($"Can't read '{source.name}' — tick Read/Write Enabled on its import settings.",
                    ZuiText.Subtle, "The pixel scan the disc filters needs a readable texture.");
            if (inside == 0)
                return Z.Text($"The disc covers NONE of '{source.name}'s {total} opaque pixels — the spray " +
                              "falls back to the whole footprint.", ZuiText.Subtle,
                    "Measured with the same test the burst uses. Move the offset back onto the art, or widen " +
                    "the radius, to localise it again.");
            return Z.Text($"The disc covers {inside} of '{source.name}'s {total} opaque pixels.", ZuiText.Subtle,
                "Measured with the same test the burst uses, so this is what will actually be sampled.");
        }

        // ── sampled crops: real cut-outs of the art, optionally masked and scorched ──────────────────────────
        void BuildSampledCropBlock(VisualElement body, ChunkSpec c, PaletteSplash cap, string id)
        {
            body.Add(Z.MicroMinMax("Crop px", cap.cropPxMin, cap.cropPxMax, 1f, 16f,
                "Size of each cut, in SOURCE-texture pixels. A cut is drawn at exactly that many game pixels — " +
                "its texel count and its on-screen size are the same number, so a crop can never be squashed " +
                "below its own pixel grid, and never below one game pixel either.",
                (lo, hi) => Dial("Edit Crop Px", () =>
                {
                    cap.cropPxMin = Mathf.Max(1, Mathf.RoundToInt(lo));
                    cap.cropPxMax = Mathf.Max(cap.cropPxMin, Mathf.RoundToInt(hi));
                }), 150f, showValue: true, decimals: 0));

            // ── edge masking ───────────────────────────────────────────────────
            var mask = Z.BoxKeyed("Torn edges",
                "Eat each cut's square outline away toward transparency, so a fragment reads as a torn scrap " +
                "rather than a crisp little rectangle of the art.",
                "Chunks.card." + id + ".cropmask");
            mask.AddHeaderContent(Z.Toggle("On",
                "Fade each cut's edges out. Off = a hard-edged rectangle, exactly the raw cut.",
                cap.maskCropEdges, v => DialAndRebuildCard(id, "Toggle Torn Edges", () => cap.maskCropEdges = v)));
            if (cap.maskCropEdges)
            {
                mask.Add(Z.MicroSlider("Bite", cap.edgeMaskStrength, 0f, 1f,
                    "How far in the fade eats. Low values just round the corners off; 1 leaves barely a dot.",
                    v => Dial("Edit Torn Edge Bite", () => cap.edgeMaskStrength = v), 150f, showValue: true, decimals: 2));
                mask.Add(Z.MicroSlider("Raggedness", cap.edgeMaskJitter, 0f, 1f,
                    "How broken-up the faded boundary is. 0 = a clean circle. Hashed from each pixel's own " +
                    "coordinates, so the same crop tears the same way every run.",
                    v => Dial("Edit Torn Edge Raggedness", () => cap.edgeMaskJitter = v), 150f, showValue: true, decimals: 2));
            }
            body.Add(mask);

            // ── edge tinting ───────────────────────────────────────────────────
            var tint = Z.BoxKeyed("Scorched edges",
                "Blend a colour ramp onto each cut's EDGE pixels, for scorched or wounded edges. Where in the " +
                "ramp a cut reads from is how far it came from the middle of the hit.",
                "Chunks.card." + id + ".croptint");
            tint.AddHeaderContent(Z.Toggle("On",
                "Tint the rim of each cut. Off = the raw sampled colours, untouched.",
                cap.tintCropEdges, v => DialAndRebuildCard(id, "Toggle Scorched Edges", () => cap.tintCropEdges = v)));
            if (cap.tintCropEdges)
            {
                tint.Add(Z.Field("Ramp",
                    "Which colour ramp scorches the rim. Rust, Blood and Charred are built in; Custom lets you " +
                    "author your own below.",
                    Z.MiniRadio((int)cap.edgeTintPreset, ChunkEdgeTints.Names,
                        "Which colour ramp scorches the rim.",
                        i => DialAndRebuildCard(id, "Set Edge Tint Ramp", () =>
                        {
                            var picked = (ChunkEdgeTintPreset)i;
                            // Switching TO Custom seeds the author's gradient from whatever preset was showing,
                            // so "start from Charred and tweak it" works — with a detached copy, never the
                            // shared preset instance, which every other recipe in the project also reads.
                            if (picked == ChunkEdgeTintPreset.Custom
                                && cap.edgeTintPreset != ChunkEdgeTintPreset.Custom)
                                cap.customEdgeTint = ChunkEdgeTints.Clone(ChunkEdgeTints.Of(cap.edgeTintPreset));
                            cap.edgeTintPreset = picked;
                        }), true)));

                if (cap.edgeTintPreset == ChunkEdgeTintPreset.Custom)
                {
                    // Unity's own gradient editor through ZUI's wrapper — the established control for a plain
                    // Gradient field everywhere in this codebase (Debris Scatter's Colour over life uses the
                    // same one), and it already carries the project's saved-gradient library.
                    tint.Add(Z.Field("Custom ramp",
                        "Left = a cut from the dead centre of the hit, right = one from the outer rim. Its " +
                        "ALPHA is a per-position strength on top of the Strength dial below.",
                        Z.Gradient(cap.customEdgeTint,
                            "Left = the centre of the hit, right = its outer rim. Alpha scales the strength.",
                            v => Dial("Edit Custom Edge Ramp", () => cap.customEdgeTint = v))));
                }
                else
                {
                    // Read-only: a preset is shared by every recipe that picks it, so editing it here would
                    // silently change somebody else's splash. Pick Custom to make it editable (which copies it).
                    var swatch = Z.Gradient(ChunkEdgeTints.Of(cap.edgeTintPreset),
                        "What this preset looks like, left (centre of the hit) to right (its outer rim). Pick " +
                        "Custom to get an editable copy of it.", null);
                    swatch.SetEnabled(false);
                    tint.Add(Z.Field(ChunkEdgeTints.Names[(int)cap.edgeTintPreset],
                        "What this preset looks like, left (centre of the hit) to right (its outer rim).",
                        swatch));
                }

                tint.Add(Z.HGroup(
                    Z.MicroSlider("Strength", cap.edgeTintStrength, 0f, 1f,
                        "How far the ramp pulls an edge pixel. 0 = no visible effect, 1 = fully replaced.",
                        v => Dial("Edit Edge Tint Strength", () => cap.edgeTintStrength = v),
                        150f, showValue: true, decimals: 2),
                    Z.Field("Edge px",
                        "How many pixels in from a cut's rim count as its edge.",
                        Z.Int(cap.edgeTintThicknessPx, "How many pixels in from a cut's rim count as its edge.",
                            v => Dial("Edit Edge Tint Thickness",
                                () => cap.edgeTintThicknessPx = Mathf.Max(1, v)), 70f))));
            }
            body.Add(tint);
        }
    }
}
