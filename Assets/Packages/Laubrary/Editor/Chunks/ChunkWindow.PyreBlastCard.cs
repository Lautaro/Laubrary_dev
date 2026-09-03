// ChunkWindow.PyreBlastCard — the card for a Pyre Blast, and the worked example every other card copies.
//
// The shape to copy, in order: the wide reference picker gets its own row because it is wide by nature; the
// one control that decides which OTHER controls exist comes next and rebuilds only this card when it changes;
// the fields that shape belongs to appear directly under it and are ABSENT, not disabled, when the shape does
// not have them; the rest pack into rows of short controls; the layer slot is last because it answers "where
// does this draw", not "what is this".
//
// Every write goes through Dial (or DialAndRebuildCard where the answer changes which controls exist). The
// card never touches the stack, the clock, the preview or undo directly.
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        void BuildPyreBlastCard(VisualElement body, ChunkSpec c, PyreBlast cap)
        {
            string id = cap.id;

            // ── what gets spawned ────────────────────────────────────────────────
            body.Add(Z.Field("Blast",
                "The effect spawned at each point of the pattern. Ignored while the alternates below hold anything.",
                AssetPicker(cap.source, o => DialAndRebuildCard(id, "Set Blast", () => cap.source = o),
                            typeof(IChunkEffectSpawner), "Blast",
                            "The effect spawned at each point of the pattern.")));

            int poolCount = cap.pool != null ? cap.pool.Count : 0;
            var alternates = new VisualElement();
            for (int i = 0; i < poolCount; i++)
            {
                int index = i;
                var row = Z.Row(
                    AssetPicker(cap.pool[index], o => DialAndRebuildCard(id, "Set Blast Alternate",
                                                                        () => cap.pool[index] = o),
                                typeof(IChunkEffectSpawner), "Blast",
                                "One of the effects each spawn picks between."),
                    SmallButton("×", "Take this one out of the alternates.", true,
                        () => DialAndRebuildCard(id, "Remove Blast Alternate", () => cap.pool.RemoveAt(index))));
                alternates.Add(row);
            }
            var addAlternate = Z.Button("Add alternate",
                "Let each spawn pick between several effects instead of always using the one above. While " +
                "this list holds anything it wins outright.",
                () => DialAndRebuildCard(id, "Add Blast Alternate", () =>
                {
                    cap.pool ??= new System.Collections.Generic.List<Object>();
                    cap.pool.Add(null);
                }));
            addAlternate.style.width = 120f;
            addAlternate.style.alignSelf = Align.FlexStart;
            alternates.Add(addAlternate);
            // Boxed only once there is a list to group. An empty pool is one button, and a box titled for a
            // single control says the same thing twice.
            body.Add(poolCount > 0
                ? (VisualElement)Z.Box("Alternates",
                    "A pool each spawn picks one entry from, so a repeated blast does not read as the same " +
                    "picture over and over. While it holds anything it wins over the single blast above.",
                    alternates)
                : alternates);

            // ── the pattern, and only its own fields ─────────────────────────────
            body.Add(Z.Field("Pattern",
                "One blast, several along a line, or several around a ring.",
                Z.Segmented((int)cap.Pattern, new[] { "Single", "Line", "Ring" },
                    "One blast, several along a line, or several around a ring.",
                    i => DialAndRebuildCard(id, "Set Blast Pattern",
                                            () => cap.Pattern = (PyreBlastPattern)i))));

            if (cap.UsesPattern && cap.formation != null)
            {
                var f = cap.formation;
                bool ring = cap.Pattern == PyreBlastPattern.Ring;

                var placement = Z.HGroup(
                    Z.MicroSlider("Count", f.count, 1f, 64f,
                        "How many blasts the pattern lays out.",
                        v => DialAndRebuildCard(id, "Edit Pattern",
                                                () => f.count = Mathf.Clamp(Mathf.RoundToInt(v), 1, 64)),
                        150f, showValue: true, decimals: 0),
                    ring
                        ? Z.MicroSlider("Radius", f.radius, 0.1f, 20f,
                            "How far from the centre the ring's blasts sit, in world units.",
                            v => Dial("Edit Pattern", () => f.radius = v), 150f, showValue: true, decimals: 2)
                        : Z.MicroSlider("Length", f.length, 0.1f, 40f,
                            "How long the line is, in world units.",
                            v => Dial("Edit Pattern", () => f.length = v), 150f, showValue: true, decimals: 2),
                    ring
                        ? Z.MicroSlider("Arc", f.arcDeg, 0f, 360f,
                            "How much of a full circle the blasts spread over. 360 is a closed ring.",
                            v => Dial("Edit Pattern", () => f.arcDeg = v), 150f, showValue: true, decimals: 0)
                        : Z.MicroSlider("Angle", f.angleDeg, -180f, 180f,
                            "Which way the line runs. 0 is horizontal.",
                            v => Dial("Edit Pattern", () => f.angleDeg = v), 150f, showValue: true, decimals: 0));
                body.Add(placement);

                var scatter = Z.HGroup(
                    ring
                        ? Z.MicroSlider("Start angle", f.startAngleDeg, 0f, 360f,
                            "Where around the ring the first blast sits.",
                            v => Dial("Edit Pattern", () => f.startAngleDeg = v), 150f, showValue: true, decimals: 0)
                        : null,
                    Z.MicroSlider("Jitter", f.positionJitter, 0f, 3f,
                        "How far each blast can wander off its exact point, in world units. Same seed, same wander.",
                        v => Dial("Edit Pattern", () => f.positionJitter = Mathf.Max(0f, v)),
                        150f, showValue: true, decimals: 2));
                body.Add(scatter);

                body.Add(Z.HGroup(
                    Z.MicroSlider("Stagger", f.staggerSeconds, 0f, 1f,
                        "Seconds between one blast going off and the next. 0 fires them all together.",
                        v => Dial("Edit Pattern", () => f.staggerSeconds = Mathf.Max(0f, v)),
                        150f, showValue: true, decimals: 2),
                    Z.MicroSlider("Stagger jitter", f.staggerJitter, 0f, 1f,
                        "How much each blast's own moment can drift off the even spacing.",
                        v => Dial("Edit Pattern", () => f.staggerJitter = Mathf.Max(0f, v)),
                        150f, showValue: true, decimals: 2)));

                body.Add(Z.Field("Order",
                    "The order the blasts go off in once they are staggered.",
                    Z.MiniRadio((int)f.staggerOrder,
                        new[] { "Sequential", "Reverse", "From centre", "Random" },
                        "The order the blasts go off in once they are staggered.",
                        i => Dial("Edit Pattern", () => f.staggerOrder = (FormationStaggerOrder)i), true)));

                body.Add(Z.Field("Pattern seed",
                    "Fixes the wander and the drift so the pattern comes out the same every time. 0 rerolls.",
                    Z.Int(f.seed, "Fixes the wander and the drift so the pattern comes out the same every time.",
                          v => Dial("Edit Pattern", () => f.seed = v), 70f)));
            }

            // ── where, and how each one comes out ────────────────────────────────
            body.Add(Z.Field("Offset",
                "Where the pattern's centre sits relative to the recipe's origin, in world units.",
                Z.Pad(cap.offset, new Rect(-8f, -8f, 16f, 16f),
                      "Where the pattern's centre sits relative to the recipe's origin, in world units.",
                      v => Dial("Edit Blast Offset", () => cap.offset = v))));

            body.Add(Z.Field("Rotation",
                "Whether each blast follows the recipe's aim, sits at a fixed angle, or picks one at random.",
                Z.Segmented((int)cap.rotationMode, new[] { "Inherit", "Fixed", "Random" },
                    "Whether each blast follows the recipe's aim, sits at a fixed angle, or picks one at random.",
                    i => DialAndRebuildCard(id, "Set Blast Rotation",
                                            () => cap.rotationMode = (PyreSpawnRotation)i))));

            if (cap.rotationMode == PyreSpawnRotation.Fixed)
                body.Add(Z.MicroSlider("Angle", cap.fixedAngleDeg, 0f, 360f,
                    "The angle every blast is rotated to. 0 points right, 90 points up.",
                    v => Dial("Edit Blast Angle", () => cap.fixedAngleDeg = v), 170f, showValue: true, decimals: 0));
            else if (cap.rotationMode == PyreSpawnRotation.RandomRange)
                body.Add(Z.MicroMinMax("Angle range", cap.randomAngleMinDeg, cap.randomAngleMaxDeg, 0f, 360f,
                    "The band of angles a blast's rotation is drawn from.",
                    (lo, hi) => Dial("Edit Blast Angle Range",
                                     () => { cap.randomAngleMinDeg = lo; cap.randomAngleMaxDeg = hi; }),
                    200f, showValue: true, decimals: 0));

            body.Add(Z.HGroup(
                Z.MicroMinMax("Scale", cap.scaleMin, cap.scaleMax, 0.01f, 8f,
                    "The size band a blast comes out at. 1 is the effect's own authored size; both ends equal " +
                    "makes every blast the same size.",
                    (lo, hi) => Dial("Edit Blast Scale",
                                     () => { cap.scaleMin = Mathf.Max(0.01f, lo); cap.scaleMax = Mathf.Max(cap.scaleMin, hi); }),
                    180f, showValue: true, decimals: 2),
                Z.MicroSlider("On screen", cap.blastSeconds, 0f, 8f,
                    "Roughly how long one blast lasts. This sizes the recipe's clock only — it never cuts a " +
                    "blast short.",
                    v => Dial("Edit Blast Length", () => cap.blastSeconds = Mathf.Max(0f, v)),
                    150f, showValue: true, decimals: 2)));

            body.Add(Z.Field("Seed",
                "Fixes which alternate, which angle and which size each spawn draws, so the blast comes out the " +
                "same every time. 0 rerolls.",
                Z.Int(cap.seed, "Fixes the picking, angle and size so the blast comes out the same every time.",
                      v => Dial("Edit Blast Seed", () => cap.seed = v), 70f)));

            var slot = LayerSlotRow(c, () => cap.layerName, v => cap.layerName = v);
            if (slot != null) body.Add(slot);
        }
    }
}
