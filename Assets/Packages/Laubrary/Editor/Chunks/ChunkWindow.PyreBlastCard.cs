// ChunkWindow.PyreBlastCard — the card for a Pyre Blast, and the worked example every other card copies.
//
// The shape to copy, in order: the wide reference picker gets its own row because it is wide by nature; the
// one control that decides which OTHER controls exist comes next and rebuilds only this card when it changes;
// the fields that shape belongs to appear directly under it and are ABSENT, not disabled, when the shape does
// not have them; the rest pack into rows of short controls; the layer slot is last because it answers "where
// does this draw", not "what is this".
//
// Every write goes through Dial (or DialAndRebuildCard where the answer changes which controls exist), with
// ONE deliberate exception: keeping "On screen" equal to the blast's own real play length while Auto is on
// (T-0356) is a data-CONSISTENCY correction, not a user edit, so it is applied directly (see the top of
// BuildPyreBlastCard) rather than through Dial — it must not create its own Undo step or fire a repaint/
// timing cascade every time this card is merely built, only when the number it corrects to actually changes.
// Every EXPLICIT edit that can change the answer (picking a Blast, adding/removing/setting an alternate)
// still re-syncs it inside that edit's own Dial call, so the common case never depends on the passive path.
using System.Collections.Generic;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        // The offset pattern's own bounds — shared with the stage's drag hit-test (ChunkWindow.Preview.cs)
        // so a dragged disc clamps to exactly the same box the pad itself clamps to.
        internal static readonly Rect BlastOffsetRange = new Rect(-8f, -8f, 16f, 16f);

        // One sync delegate per Pyre Blast card, so a drag on the PREVIEW STAGE (ChunkWindow.Preview.cs) can
        // push the new offset into this card's pad + X/Y fields without rebuilding the card — same reason
        // BackSplashWindow keeps posPad/posXField/posYField as retained fields, just keyed by capability id
        // because a recipe can hold more than one Pyre Blast.
        readonly Dictionary<string, System.Action<Vector2>> blastOffsetSyncs = new Dictionary<string, System.Action<Vector2>>();

        void BuildPyreBlastCard(VisualElement body, ChunkSpec c, PyreBlast cap)
        {
            string id = cap.id;

            SyncAutoBlastSeconds(c, cap);

            // ── what gets spawned ────────────────────────────────────────────────
            // Picker and Pattern come FIRST and stay in that order regardless of which pattern is chosen —
            // the "rows above the switch never move" stable-workspace rule — so everything that only makes
            // sense once several points exist (alternates, placement dials, order, pattern seed) lives BELOW
            // the Pattern row and is absent, not disabled, for a Single blast (T-0360, F7/F11).
            body.Add(Z.Field("Blast",
                "The effect spawned at each point of the pattern. Ignored while the alternates below hold anything.",
                AssetPicker(cap.source, o => DialAndRebuildCard(id, "Set Blast", () =>
                {
                    cap.source = o;
                    TrySyncBlastSeconds(cap);
                }), typeof(IChunkEffectSpawner), "Blast",
                            "The effect spawned at each point of the pattern.")));

            body.Add(Z.Field("Pattern",
                "One blast, several along a line, or several around a ring.",
                Z.Segmented((int)cap.Pattern, new[] { "Single", "Line", "Ring" },
                    "One blast, several along a line, or several around a ring.",
                    i => DialAndRebuildCard(id, "Set Blast Pattern",
                                            () => cap.Pattern = (PyreBlastPattern)i))));

            // Alternates only earn their row once there is more than one spawn point to vary across, or once
            // a pool has actually been authored (so switching back to Single never strands existing data).
            int poolCount = cap.pool != null ? cap.pool.Count : 0;
            if (cap.UsesPattern || poolCount > 0)
            {
                var alternates = new VisualElement();
                for (int i = 0; i < poolCount; i++)
                {
                    int index = i;
                    var row = Z.Row(
                        AssetPicker(cap.pool[index], o => DialAndRebuildCard(id, "Set Blast Alternate", () =>
                        {
                            cap.pool[index] = o;
                            TrySyncBlastSeconds(cap);
                        }), typeof(IChunkEffectSpawner), "Blast",
                                    "One of the effects each spawn picks between."),
                        SmallButton("×", "Take this one out of the alternates.", true,
                            () => DialAndRebuildCard(id, "Remove Blast Alternate", () =>
                            {
                                cap.pool.RemoveAt(index);
                                TrySyncBlastSeconds(cap);
                            })));
                    alternates.Add(row);
                }
                var addAlternate = Z.Button("Add alternate",
                    "Let each spawn pick between several effects instead of always using the one above. While " +
                    "this list holds anything it wins outright.",
                    () => DialAndRebuildCard(id, "Add Blast Alternate", () =>
                    {
                        cap.pool ??= new System.Collections.Generic.List<Object>();
                        cap.pool.Add(null);
                        // No TrySyncBlastSeconds here — the new slot starts empty (RealSeconds returns null
                        // for it), so it cannot lengthen the pool's longest, only the next asset picked into
                        // it can (handled above, in that picker's own callback).
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
            }

            if (cap.UsesPattern && cap.formation != null)
            {
                var f = cap.formation;
                bool ring = cap.Pattern == PyreBlastPattern.Ring;

                var placement = Z.HGroup(
                    Z.MicroSlider("Count", f.count, 1f, SpawnFormation.MaxCount,
                        "How many blasts the pattern lays out.",
                        v => DialAndRebuildCard(id, "Edit Pattern",
                                                () => f.count = Mathf.Clamp(Mathf.RoundToInt(v), 1, SpawnFormation.MaxCount)),
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

                body.Add(SeedField(f.seed, "the wander and the drift", "Edit Pattern", v => f.seed = v,
                                   "Pattern seed"));
            }

            // ── where, and how each one comes out ────────────────────────────────
            // Z.PadRow (not a bare Z.Pad) — the pattern's centre also needs to be settable exactly, and by
            // dragging its disc directly on the preview stage (see ChunkWindow.Preview.cs), so the row keeps
            // a sync delegate the stage drag can push into without rebuilding this card.
            body.Add(Z.Field("Offset",
                "Where the pattern's centre sits relative to the recipe's origin, in world units. Drag the " +
                "pad, type/scrub X and Y, or drag the blast's own disc on the preview stage below.",
                Z.PadRow(out var offsetSync, cap.offset, BlastOffsetRange,
                    "Where the pattern's centre sits relative to the recipe's origin, in world units.",
                    v => Dial("Edit Blast Offset", () => cap.offset = v))));
            blastOffsetSyncs[id] = offsetSync;

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

            // "On screen" — the recipe's clock, its timing bands and the preview flight are all sized from
            // this ONE number (see PyreBlast.DurationSeconds and ChunkPreviewSim), so keeping it true is what
            // makes every one of those agree with what the chosen Blast actually plays (T-0356). Auto (the
            // default) reads it straight from the Blast/Alternates above; the slider goes read-only and shows
            // that measured number so nothing here can drift from the picker above it. Off hands the number
            // back to typing, for the two real cases where Auto cannot be trusted: a source it cannot measure
            // (the tooltip says so), or a deliberate pacing choice.
            float? measured = ChunkPyreBlastLength.RealSeconds(cap);
            bool autoShowsMeasured = cap.blastSecondsAuto && measured.HasValue;
            string onScreenTooltip = cap.blastSecondsAuto
                ? (measured.HasValue
                    ? "The chosen Blast's own real play length (the longest of the Alternates, once there is " +
                      "more than one), read automatically. Turn Auto off to type a number by hand instead."
                    : "Auto is on, but this picker's source is not one this window can read a length from, so " +
                      "the number below is whatever was last typed. Turn Auto off to make that explicit, or " +
                      "pick a Pyre / Pyre Spawn Source to measure one.")
                : "Typed by hand. This sizes the recipe's clock, its timing bands and the preview flight only " +
                  "— it never cuts a blast short. Turn Auto on to match the chosen Blast's own real play " +
                  "length instead.";
            var onScreenSlider = Z.MicroSlider("On screen", autoShowsMeasured ? measured.Value : cap.blastSeconds,
                0f, 8f, onScreenTooltip,
                v => Dial("Edit Blast Length", () => cap.blastSeconds = Mathf.Max(0f, v)),
                150f, showValue: true, decimals: 2);
            onScreenSlider.SetEnabled(!cap.blastSecondsAuto);

            body.Add(Z.HGroup(
                Z.MicroMinMax("Scale", cap.scaleMin, cap.scaleMax, 0.01f, 8f,
                    "The size band a blast comes out at. 1 is the effect's own authored size; both ends equal " +
                    "makes every blast the same size.",
                    (lo, hi) => Dial("Edit Blast Scale",
                                     () => { cap.scaleMin = Mathf.Max(0.01f, lo); cap.scaleMax = Mathf.Max(cap.scaleMin, hi); }),
                    180f, showValue: true, lowDefault: 1f, highDefault: 1f, decimals: 2),
                onScreenSlider,
                Z.ToggleButton("Auto",
                    cap.blastSecondsAuto
                        ? "On screen matches the chosen Blast's own real play length automatically. Click to " +
                          "type a number by hand instead."
                        : "On screen is typed by hand. Click to match the chosen Blast's own real play length " +
                          "automatically instead.",
                    cap.blastSecondsAuto, v => DialAndRebuildCard(id, "Set Blast Length Auto", () =>
                    {
                        cap.blastSecondsAuto = v;
                        TrySyncBlastSeconds(cap);
                    }))));

            // Colour sits with Scale rather than with the pattern, because both answer "how does one blast
            // come out" — the pattern above answers "how many, and where".
            body.Add(Z.HGroup(
                Z.Field("Tint",
                    "Multiplied onto every blast this spawns, so one effect can come out in several colours. " +
                    "White leaves it exactly as authored.",
                    Z.Color(cap.tint,
                        "Multiplied onto every blast this spawns. White leaves it exactly as authored.",
                        v => Dial("Edit Blast Tint", () => cap.tint = v), 110f)),
                Z.MicroMinMax("Alpha", cap.alphaMin, cap.alphaMax, 0f, 1f,
                    "The opacity band a blast comes out at, on top of the tint's own. Both ends at 1 leaves " +
                    "every blast fully solid.",
                    (lo, hi) => Dial("Edit Blast Alpha", () =>
                    {
                        cap.alphaMin = Mathf.Clamp01(lo);
                        cap.alphaMax = Mathf.Clamp01(Mathf.Max(cap.alphaMin, hi));
                    }),
                    180f, showValue: true, lowDefault: 1f, highDefault: 1f, decimals: 2)));

            body.Add(SeedField(cap.seed, "which alternate, which angle, which size and which opacity each spawn " +
                               "draws", "Edit Blast Seed", v => cap.seed = v));

            var slot = LayerSlotRow(c, () => cap.layerName, v => cap.layerName = v);
            if (slot != null) body.Add(slot);
        }

        /// While Auto is on, write the chosen Blast/Alternates' own real length into blastSeconds — a no-op
        /// when Auto is off, the source's length cannot be measured, or the stored number already matches
        /// (T-0356). Called from INSIDE an edit's own Dial `apply` (Blast/Alternates/Auto-toggle callbacks
        /// above), so it rides that edit's existing Undo/dirty/SyncTiming — it never records an Undo step of
        /// its own.
        static void TrySyncBlastSeconds(PyreBlast cap)
        {
            if (cap == null || !cap.blastSecondsAuto) return;
            float? real = ChunkPyreBlastLength.RealSeconds(cap);
            if (real.HasValue) cap.blastSeconds = real.Value;
        }

        /// The passive twin of <see cref="TrySyncBlastSeconds"/>, run once whenever this card is BUILT rather
        /// than only when it is explicitly edited — so a capability that has never been touched since T-0356
        /// shipped (an old recipe authored against the 0.6s default, or one whose picked Pyre's own
        /// frameCount/fps changed from outside this window) self-corrects the moment its card is next drawn,
        /// instead of silently keeping a stale number until someone happens to re-pick its Blast. Written
        /// directly rather than through Dial (see this file's header comment) — SetDirty only, no Undo, and
        /// only when the number actually changes, so opening a window that has nothing to correct touches
        /// nothing. RebuildStack calls SyncTiming AFTER every card finishes building, so a correction made
        /// here during that pass still reaches the timing lanes for the initial build/open case; the
        /// DialAndRebuildCard-driven edits above additionally self-sync inline because THEIR SyncTiming runs
        /// before this card is rebuilt, not after.
        static void SyncAutoBlastSeconds(ChunkSpec c, PyreBlast cap)
        {
            if (cap == null || !cap.blastSecondsAuto) return;
            float? real = ChunkPyreBlastLength.RealSeconds(cap);
            if (!real.HasValue || Mathf.Approximately(real.Value, cap.blastSeconds)) return;
            cap.blastSeconds = real.Value;
            if (c != null) EditorUtility.SetDirty(c);
        }

        /// Push an offset dragged directly on the preview stage (ChunkWindow.Preview.cs) into this
        /// capability's own Offset pad + X/Y fields, without rebuilding the card. A no-op once the stack is
        /// rebuilt and the id's sync delegate is gone (the card itself already shows the new value then).
        internal void SyncBlastOffsetRow(string capabilityId, Vector2 value)
        {
            if (blastOffsetSyncs.TryGetValue(capabilityId, out var sync)) sync(value);
        }
    }
}
