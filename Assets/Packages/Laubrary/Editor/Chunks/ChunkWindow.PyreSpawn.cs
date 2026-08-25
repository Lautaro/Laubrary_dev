// ChunkWindow.PyreSpawn — the "Blasts" section: ONE list of uniform blast cards (AgentHQ T-0034 / T-0081,
// design doc "Standalone modules" #2).
//
// A spec holds SEVERAL blasts: `c.pyreSpawn` is the spec's own first one, and every entry of
// `c.blastGroups` is another. That is what makes "this explosion BEHIND the flying pieces, those ones IN
// FRONT of them" authorable at all — one blast reference per depth.
//
// T-0081 rewrote the presentation (the DATA is untouched). It used to be one Z.Section per blast, the first
// titled "Pyre Spawn" and the rest "Blast 2 — …", "Blast 3 — …", with a loose add button under the whole
// window and ▲ ▼ × buttons shoved off the right edge of a body row. That numbering had no "Blast 1", went
// stale on every reorder, and hid add/remove from the user entirely. It is now ONE section holding a list of
// cards in the Pyre card shape — `Z.Box(null,null)` + a `zui-row` header (grip / enable / name / ×) +
// a conditional body + `ZuiFoldCard.Wire` keyed on the module INSTANCE — built by ONE builder for every
// blast, so the first card and the tenth can never drift apart in what they can express.
//
// Two card-level asymmetries are real and are handled as per-card CAPABILITY inside that one builder, not as
// a second code path: the spec's own first blast is a FIELD, not a list entry, so (a) it cannot be dragged
// among the list entries — it is pinned at the top and shows no grip rather than a grip that lies — and
// (b) its × CLEARS it (fresh + off) instead of removing a card that has to exist.
//
// Every dial routes through the window's Dial / DialAndRebuild helpers (which own the Undo.RecordObject
// contract); the one exception is Z.Vector2Field, whose own contract is onBeforeMutate — it fires ONCE per
// drag gesture, so recording there gives one undo step per gesture instead of one per mouse-move.
//
// T-0081 polish pass, on top of that rewrite (the card shape, the fold keying, the grip reorder, the header
// ×, the per-blast state keys and the "+ Add blast…" placement are all unchanged):
//   • D-15 — "Several" no longer owns a near-empty full-pane row; it rides the Blast row, which the chip's
//     220px cap leaves room in. "Only a WIDE control earns its own row."
//   • D-16 — the shape dials stopped being full-width stacked rows: they are now a narrow column beside the
//     live preview, so "Start angle °" is a column entry rather than a solo row with 250px of nothing.
//   • D-17 — the offset control is EXPANDED, so it is an actual draggable 2D pad instead of the 120×18
//     strip a collapsed ZuiValue2DControl draws. That is what makes the "wide by nature" comment true.
//   • D-12 — a blast's own shape now draws the SAME live point preview the standalone Spawn Formation
//     section has: both call BuildFormationDials in ChunkWindow.Formation.cs.
//   • D-10 — the standalone Spawn Formation supersedes THIS section's first blast, and both sides now say
//     so on screen, in a reserved status line (see BlastSupersededLine).
using System.Collections.Generic;
using Laubrary.AssetKit.Editor;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Laubrary.Chunks.Editor
{
    public partial class ChunkWindow
    {
        const string PyreSpawnPickerTip =
            "The blast this fires — pick a Pyre asset directly, or a Pyre Spawn Source when you need to " +
            "override its speed or looping. Click to pick, right-click for New / Edit / Clear.";

        const float BlastAddW = 110f;    // "+ Add blast…"
        const float PoolAddW = 124f;     // "+ Add to pool…" — the two sit near each other, so they must read apart
        const float BlastGripW = 16f;

        /// The whole section: every blast as a card in one list, then the one way to add another. The spec's
        /// own `pyreSpawn` is the FIRST card and is built by the same builder as the rest.
        void BuildPyreSpawn(VisualElement root, ChunkSpec c)
        {
            // Null-repair only (mirrors ChunkSpec.OnValidate) — not an authored change, so no Undo. Done
            // BEFORE anything is built, never mid-build: a structural mutation from inside a builder would
            // re-enter Rebuild() while this one is still on the stack. A null entry is only ever a broken
            // deserialize, and it already breaks the position-keyed timeline lanes, so dropping it is the
            // repair rather than a loss.
            c.pyreSpawn ??= new PyreSpawnModule();
            c.blastGroups ??= new List<PyreSpawnModule>();
            c.blastGroups.RemoveAll(g => g == null);

            var sec = Z.Section("Blasts",
                "The explosions this burst fires. Each one has its own effect, its own placement and its own " +
                "layer slot — two blasts holding the SAME effect in different slots is the point: that is how " +
                "one goes behind the flying pieces and the other in front of them. The first blast is the " +
                "spec's own and stays at the top; drag any of the others by their grip to reorder the list.",
                "chunks.blasts");
            // A folded section must never hide how many blasts are armed inside it.
            sec.SetHeaderSuffix(() => BlastCountSuffix(Spec));
            root.Add(sec);

            // The spec's own blast lives OUTSIDE the reorder host: ZuiReorder indexes the container's direct
            // children, so a non-list card in there would corrupt every (from, to) the grips report.
            var firstHost = new VisualElement();
            sec.Add(firstHost);
            firstHost.Add(BuildBlastCard(null, c, c.pyreSpawn, -1));

            // ONLY blast-group cards may live in here.
            var listHost = new VisualElement();
            sec.Add(listHost);
            for (int i = 0; i < c.blastGroups.Count; i++)
                listHost.Add(BuildBlastCard(listHost, c, c.blastGroups[i], i));

            // The way IN, INSIDE the section and directly under the list — never loose in the window body.
            sec.Add(Z.Row(BuildAddBlastButton(c)));
        }

        /// " (N)" while the section is collapsed, N = blasts actually armed. Empty when none is, because
        /// then the folded section really is hiding nothing.
        ///
        /// D-10: it also reports the supersede, because a FOLDED section must not hide the one state where a
        /// blast the user armed is not firing. Both halves are readouts of the current configuration, not
        /// advice — the explanation lives in the status line's tooltip inside the card.
        static string BlastCountSuffix(ChunkSpec c)
        {
            if (c == null) return string.Empty;
            int n = 0;
            if (c.pyreSpawn != null && c.pyreSpawn.enabled) n++;
            if (c.blastGroups != null)
                for (int i = 0; i < c.blastGroups.Count; i++)
                    if (c.blastGroups[i] != null && c.blastGroups[i].enabled) n++;
            if (n == 0) return string.Empty;
            bool superseded = c.pyreSpawn != null && c.pyreSpawn.enabled
                              && c.spawnFormation != null && c.spawnFormation.enabled;
            return " (" + n + (superseded ? " — first not firing" : "") + ")";
        }

        /// ONE card for ONE blast — the Pyre card shape. `index < 0` is the spec's own first blast
        /// (`c.pyreSpawn`, `listHost` null, pinned at the top); 0 and up index into `c.blastGroups`.
        ///
        /// The HEADER is built whatever the blast's enabled state is, and only the BODY is conditional. That
        /// is not cosmetic: the previous UI built the remove button inside the body, so un-ticking a blast
        /// removed the only way to delete it and left it stuck in the recipe forever.
        VisualElement BuildBlastCard(VisualElement listHost, ChunkSpec c, PyreSpawnModule m, int index)
        {
            bool first = index < 0;
            var box = Z.Box(null, null);   // untitled, unkeyed — a per-item card is not captured view state

            var header = new VisualElement();
            header.AddToClassList("zui-row");
            // A wrapping header would throw the × onto a line of its own and leave a confusing hole where the
            // name should be. Stated explicitly rather than inherited from the sheet.
            header.style.flexWrap = Wrap.NoWrap;

            // ── grip ─────────────────────────────────────────────────────────────
            if (!first && listHost != null)
            {
                // The tooltip must NOT claim position is draw order: ChunkModules dispatches every blast in
                // the same frame and the LAYER SLOT is the only thing that decides what draws in front.
                var grip = Z.Text("≡", ZuiText.Body,
                    "Drag to reorder how the blasts are listed here. This is the reading order only — what " +
                    "draws in front of what is the Layer slot inside each card.");
                grip.style.unityFontStyleAndWeight = FontStyle.Bold;
                grip.style.width = BlastGripW;
                grip.style.flexShrink = 0f;
                // The drag unit is the whole CARD (header + whatever is folded under it), never the header
                // row, or a reorder would leave the body behind.
                ZuiReorder.MakeGrip(grip, box, listHost, (from, to) => MoveBlastGroup(c, from, to));
                header.Add(grip);
            }
            else
            {
                // No grip glyph where there is no drag: the spec's own blast is not a list entry. The spacer
                // keeps every card's name starting in the same column.
                var pad = Z.HSpace(BlastGripW);
                header.Add(pad);
            }

            // ── enable ───────────────────────────────────────────────────────────
            var enableToggle = Z.Toggle("",
                first
                    ? "Spawn this blast when the burst fires."
                    : "Fire this blast when the burst goes off.",
                m.enabled,
                v => DialAndRebuild("Blast enabled", () => m.enabled = v));
            header.Add(enableToggle);

            // ── name ─────────────────────────────────────────────────────────────
            // The card's name is DECLARED here (not a reference to a name declared elsewhere), so a text
            // field is the right control — but it belongs ON the header, like Pyre's layer name, not as a
            // "Name" field sitting in the body above an asset row, where it reads as "type the asset's name".
            // Empty shows the blast's own asset name as a placeholder, so a card is never nameless and never
            // numbered.
            var name = Z.TextInput(m.label ?? "",
                "What this blast is called on its card and on its timeline lane. Only there to tell several " +
                "blasts apart — it never changes what is spawned. Leave it empty to show the blast asset's " +
                "own name.",
                v => Dial("Blast name", () => m.label = v), 0f);
            // Delayed: the callback would otherwise fire per keystroke, and Dial re-slices the preview on
            // every call — one commit, one undo step, one refresh.
            name.isDelayed = true;
            name.textEdition.placeholder = BlastDerivedName(m);
            name.textEdition.hidePlaceholderOnFocus = true;
            name.style.width = StyleKeyword.Auto;
            name.style.flexGrow = 1f;
            name.style.flexShrink = 1f;
            name.style.minWidth = 60f;
            // Capped, unlike Pyre's layer name: past a couple of hundred pixels a name field only steals the
            // header's one FOLD ZONE. The flexible gap after it is what the user clicks to collapse the card
            // (the caret is pickingMode Ignore and every other header control swallows its own click), so the
            // header must always keep some.
            name.style.maxWidth = 220f;
            name.AddToClassList("zui-audit-allow-stretch");   // the rulebook's name-field stretch exception
            header.Add(name);
            header.Add(Z.Flexible());   // pins the × to the right edge — and is the card's fold zone

            // ── remove ───────────────────────────────────────────────────────────
            Button removeBtn;
            if (first)
                removeBtn = Z.Button("×",
                    "Clear this blast. The first blast is built into the spec, so this empties it and " +
                    "switches it off rather than removing the card (undoable).",
                    () => ClearFirstBlast(c)).W(22f);
            else
                removeBtn = Z.Button("×", "Remove this blast from the recipe (undoable).", () =>
                {
                    // Re-resolved at click time, never trusting the captured index: the card may have been
                    // dragged since it was built.
                    int at = c.blastGroups.IndexOf(m);
                    if (at >= 0) MutateBlastGroups(c, "Remove blast", () => c.blastGroups.RemoveAt(at));
                }).W(22f);
            header.Add(removeBtn);
            box.Add(header);

            // ── body ─────────────────────────────────────────────────────────────
            // Off ⇒ don't build it at all (the Cherry Framing shape): nothing under a disabled blast means
            // anything. The header above still carries the toggle and the ×, so the card stays operable.
            VisualElement body = null;
            if (m.enabled)
            {
                body = new VisualElement();
                BuildBlastBody(body, c, m, index);
                box.Add(body);
            }

            // Fold state keyed on the MODULE INSTANCE, so it survives rebuilds, undo and reorder. The grip
            // guards its own pointer-down; the toggle, the × and the name field must not fold the card.
            ZuiFoldCard.Wire(m, header, body, enableToggle, removeBtn, name);
            return box;
        }

        /// The name a card shows when the user has typed none — the blast asset's own name. Mirrors
        /// PyreSpawnModule.DisplayName MINUS the authored label, which is what a placeholder means.
        static string BlastDerivedName(PyreSpawnModule m)
        {
            if (m == null) return "Blast";
            if (m.source != null) return m.source.name;
            if (m.pool != null)
                for (int i = 0; i < m.pool.Count; i++)
                    if (m.pool[i] != null) return m.pool[i].name + "…";
            return "Blast";
        }

        /// Everything one blast can express. Identical for every card — the only thing `index` decides is
        /// which sub-box fold keys are used and whether the supersede status line is reserved (only the
        /// spec's own first blast can be superseded, so only card 0 reserves it).
        void BuildBlastBody(VisualElement body, ChunkSpec c, PyreSpawnModule m, int index)
        {
            bool first = index < 0;
            bool poolWins = m.pool != null && m.pool.Count > 0;

            // ── is anything overriding this blast? ───────────────────────────────
            // D-10, and only card 0 can be affected: ChunkModules.Run substitutes the standalone Spawn
            // Formation for `c.pyreSpawn`, never for a blast GROUP. So the line is reserved on the first
            // card only — that is a structural per-card difference, not something that appears and vanishes.
            if (first) body.Add(BlastSupersededLine(c));

            // ── what gets spawned, and whether it is one or several ──────────────
            // ONE row, not two (D-15). The chip is the widest thing here but it is capped at 220px, and
            // "Several" is a single latch: "only a WIDE control earns its own row", and a latch is not one.
            // It also reads as one sentence — this blast, fired several times.
            body.Add(Z.Row(
                Z.Field("Blast",
                    poolWins
                        ? "The single blast this fires — currently OVERRIDDEN by the pool below, which has " +
                          "entries. Empty the pool to use this one again."
                        : "The blast this fires at its spawn point.",
                    LauAssetElement.Build(m.source,
                        picked => DialAndRebuild("Blast asset", () => m.source = picked),
                        typeof(IChunkEffectSpawner), _visualThumbs, c.name, "Assets/Chunks/PyreSpawnSources",
                        PyreSpawnPickerTip)),
                Z.HSpace(),
                Z.Toggle("Several",
                    "Fire this blast at several points in a shape instead of once at its offset — a ring of " +
                    "explosions around the impact, say, rather than one on it. Their shape and timing are " +
                    "the \"Shape & stagger\" box below.",
                    m.useFormation,
                    v => DialAndRebuild("Blast several", () => m.useFormation = v))));

            body.Add(BuildPyreSpawnPool(c, m, index));

            // ── where the several go ─────────────────────────────────────────────
            if (m.useFormation) body.Add(BuildBlastShape(m, index));

            // ── how it comes out ─────────────────────────────────────────────────
            // Not packed with the row above or below: in Fixed / Random mode this row already carries a
            // second wide control, and a row that only fits at one of three settings would jump the layout
            // around every time the mode changed ("Stable workspace").
            body.Add(BuildPyreSpawnRotation(c, m));

            body.Add(Z.Row(
                Z.Field("Scale",
                    "Uniform scale range each spawn is picked from. Both handles together = every spawn the " +
                    "same size; 1 = the effect's authored size.",
                    Z.MinMax(m.scaleMin, m.scaleMax, 0.1f, 5f,
                        "Smallest and largest uniform scale a spawn comes out at.",
                        (lo, hi) => Dial("Blast scale", () => { m.scaleMin = lo; m.scaleMax = hi; }),
                        Wide, lowDefault: 1f, highDefault: 1f)),
                Z.HSpace(),
                Int2("Seed",
                    "Fixes the random pick, angle and scale so every play resolves identically. 0 = reroll " +
                    "every time.",
                    m.seed, v => m.seed = v)));

            // Wide by nature (side panel + numeric block + a 96px drag plot) — it earns its own row, and
            // .Expanded() is what makes that sentence TRUE (D-17). Without it ZuiValue2DControl seeds its
            // per-instance fold state to `startExpanded = false` and draws BuildCollapsed: a 120×18 strip
            // plus a ⋯ button, which reads as a 1-D slider and honours none of the plotSize below. The whole
            // point of a 2D control is the drag surface, so the pad is the default state here and the user
            // can still collapse it to the strip by clicking the label.
            body.Add(Z.Vector2Field(m.useFormation ? "Centre offset" : "Offset",
                () => m.offset, v => m.offset = v, m,
                new ZuiValue2DControl.Options()
                    .WithRange(-5f, 5f, -5f, 5f)
                    .WithDefault(Vector2.zero)
                    .WithPlotSize(96f)
                    .Expanded(),
                m.useFormation
                    ? "Where the SHAPE's centre sits relative to the burst's own origin, in world units. " +
                      "Drag the dot."
                    : "Where the spawn sits relative to the burst's own origin, in world units. Drag the dot.",
                () => { EditorUtility.SetDirty(c); RefreshChunkPreview(); },
                () => Undo.RecordObject(c, "Blast offset")));

            // ── draw order ───────────────────────────────────────────────────────
            body.Add(BuildPyreSpawnLayerSlot(c, m, first));
        }

        /// D-10, this side of it: whether the standalone Spawn Formation section is currently firing this
        /// blast instead of this card. One RESERVED line at the top of the card, where the user is already
        /// looking, whose TEXT changes — never an element that appears (the version this replaced added a
        /// Z.Text only while the conflict was live, so switching Spawn Formation on grew a line mid-card and
        /// shoved everything under it down; that is the "Stable workspace" rule's exact failure).
        ///
        /// It says what is HAPPENING, in the user's terms ("this blast is not firing here"), never how the
        /// two modules relate — that belongs in the tooltip, per "Labeling — tooltip, not title", which
        /// forbids instructional prose on screen. And the empty state is a real readout too: "nothing is
        /// overriding this blast".
        ///
        /// The card's controls are deliberately NOT disabled while superseded. A user is usually tuning a
        /// blast for after they switch the formation back off, and greying out values they can still see
        /// reads as "this tool is broken" rather than "this is overridden".
        static Label BlastSupersededLine(ChunkSpec c)
        {
            bool superseded = c.spawnFormation != null && c.spawnFormation.enabled;
            return SupersedeStatusLine(
                superseded
                    ? "Not firing here — the Spawn Formation section is firing this blast at its own points."
                    : string.Empty,
                superseded
                    ? "While the Spawn Formation section is on it takes this blast's effect and fires it at " +
                      "ITS points, in ITS layer slot — so this card's own Offset, its Several shape and its " +
                      "Layer slot are not in use. The other blasts are unaffected. Switch that section off " +
                      "to hand this blast back its own placement."
                    : "Whether anything else in this recipe is overriding this blast. Empty means nothing is.");
        }

        /// Fold keys for one card's sub-boxes. Per blast, or every card's Pool box folds and unfolds together.
        static string BlastStateKey(int index)
            => index < 0 ? "chunks.blast.first" : "chunks.blast." + index;

        /// The way IN for the list. An empty list must never be a dead end, and picking the blast in the same
        /// gesture that creates the card means a new card is never born blank.
        Button BuildAddBlastButton(ChunkSpec c)
        {
            Button add = null;
            add = Z.Button("+ Add blast…",
                "Add ANOTHER blast to this burst, with its own effect, its own placement and its own layer " +
                "slot — the way to put one explosion behind the flying pieces and another in front of them. " +
                "Opens the blast browser to pick what it fires. (Not the same as the Pool inside a card, " +
                "which is one blast picked at random from several.)", () =>
                {
                    var wb = add.worldBound;
                    LauAssetBrowser.Show(new Rect(wb.x, wb.y, wb.width, wb.height), typeof(IChunkEffectSpawner),
                        picked => MutateBlastGroups(c, "Add blast", () =>
                        {
                            c.blastGroups ??= new List<PyreSpawnModule>();
                            c.blastGroups.Add(new PyreSpawnModule { enabled = true, source = picked });
                        }), null);
                });
            return add.W(BlastAddW);
        }

        /// Clear the spec's own first blast. It is a FIELD, not a list entry, so it cannot be removed — but
        /// its × must still DO what a × promises, and "empty it and switch it off" is the honest equivalent.
        void ClearFirstBlast(ChunkSpec c)
        {
            DialAndRebuild("Clear first blast", () =>
            {
                c.pyreSpawn = new PyreSpawnModule();   // fresh + off, exactly what a new spec ships with
                // Its timeline lane is keyed to the SLOT ("Pyre Spawn"), not to the blast, so a delay left
                // behind here would silently apply to whatever is picked next — and with the module off the
                // lane isn't even on screen to explain itself. Drop it with the blast.
                c.timeline?.tracks?.RemoveAll(t => t != null && t.moduleName == ChunkModules.PyreSpawn);
            });
        }

        /// This blast's own shape dials — WHERE its several spawns sit and WHEN each goes off.
        ///
        /// D-12. These used to be a hand-copy of ChunkWindow.Formation.cs's dials, with no preview, because
        /// that file's builder was hard-wired both to `c.spawnFormation` and to the ONE `_formationPreviewEl`
        /// field its live canvas repaints — several cards would have fought over one element. That builder is
        /// now parameterised by (formation, preview size, stagger-box key) and keeps a LIST of live preview
        /// canvases, so this is a CALL, not a copy: a blast's ring gets the same point-and-order preview the
        /// standalone section has always had, and the two can no longer drift apart.
        ///
        /// The stagger dials go in-line under a labelled divider rather than in a sub-box (staggerBoxKey
        /// null): this box already says "& stagger", and a box titled "Stagger" inside it would say the same
        /// word twice one fold level down.
        VisualElement BuildBlastShape(PyreSpawnModule m, int index)
        {
            var box = Z.BoxKeyed("Shape & stagger",
                "Where this blast's spawns sit and how long after burst-start each one goes off.",
                BlastStateKey(index) + ".several");
            BuildFormationDials(box, m.formation, "Blast", "this blast's offset",
                FormationPreviewCard, null);
            return box;
        }

        void MoveBlastGroup(ChunkSpec c, int from, int to)
        {
            if (c.blastGroups == null || to < 0 || to >= c.blastGroups.Count || from == to) return;
            MutateBlastGroups(c, "Reorder blasts", () =>
            {
                var g = c.blastGroups[from];
                c.blastGroups.RemoveAt(from);
                c.blastGroups.Insert(to, g);
            });
        }

        /// <summary>
        /// Add / remove / reorder a blast group, carrying its TIMELINE DELAY with it.
        ///
        /// Timeline lanes are keyed by position (ChunkModules.BlastGroupTrack), which is what stops a delay
        /// being orphaned when a group is renamed — but it means the keys shift the moment the list does.
        /// Without this, removing the first of three groups would silently hand group 3's delay to group 2.
        /// So: snapshot every group's delay BY THE GROUP OBJECT, mutate, then re-key from the new positions.
        /// </summary>
        void MutateBlastGroups(ChunkSpec c, string undoLabel, System.Action mutate)
        {
            DialAndRebuild(undoLabel, () =>
            {
                c.blastGroups ??= new List<PyreSpawnModule>();
                var timeline = c.timeline;
                var carried = new Dictionary<PyreSpawnModule, float>();
                if (timeline?.tracks != null)
                    for (int i = 0; i < c.blastGroups.Count; i++)
                    {
                        string key = ChunkModules.BlastGroupTrack(c.blastGroups[i], i);
                        var track = timeline.tracks.Find(t => t != null && t.moduleName == key);
                        if (track != null && track.delay != 0f) carried[c.blastGroups[i]] = track.delay;
                    }

                mutate();

                if (timeline?.tracks == null) return;
                // Drop every old blast-group lane before writing the new ones: a removal leaves a lane whose
                // position no longer exists, and a stale lane would keep applying a delay to nothing.
                timeline.tracks.RemoveAll(t => t != null && t.moduleName != null &&
                                               t.moduleName.StartsWith("Blast "));
                for (int i = 0; i < c.blastGroups.Count; i++)
                {
                    if (!carried.TryGetValue(c.blastGroups[i], out float delay)) continue;
                    timeline.tracks.Add(new ChunkTimelineTrack
                    {
                        moduleName = ChunkModules.BlastGroupTrack(c.blastGroups[i], i),
                        delay = delay
                    });
                }
            });
        }

        VisualElement BuildPyreSpawnPool(ChunkSpec c, PyreSpawnModule m, int index)
        {
            m.pool ??= new List<Object>();

            // Keyed PER BLAST: one shared literal key had every card's Pool box fold and unfold together.
            var box = Z.BoxKeyed("Pool",
                "Pick ONE of these at random per spawn instead of the single blast above. Any entry here " +
                "wins over the single blast; an empty pool hands it back. This is variety within THIS " +
                "blast — not another blast, which is '+ Add blast…' at the bottom of the section.",
                BlastStateKey(index) + ".pool");

            for (int i = 0; i < m.pool.Count; i++)
            {
                int at = i;
                var chip = LauAssetElement.Build(m.pool[at],
                    picked => DialAndRebuild("Pool blast", () => m.pool[at] = picked),
                    typeof(IChunkEffectSpawner), _visualThumbs, c.name, "Assets/Chunks/PyreSpawnSources",
                    PyreSpawnPickerTip);
                var remove = Z.Button("×", "Remove this blast from the pool (undoable).",
                    () => DialAndRebuild("Remove pool blast", () => m.pool.RemoveAt(at))).W(22f);
                box.Add(Z.Row(chip, Z.HSpace(4f), remove));
            }

            // The way IN — an empty pool must never be a dead end. Picking an asset in the browser adds it on
            // that one click, so the label's promise is kept by the press (the "…" says more UI opens first).
            Button add = null;
            add = Z.Button("+ Add to pool…",
                "Pick one more blast for THIS blast's random pool — each spawn then draws one of them at " +
                "random. It does not add another blast to the burst; that is '+ Add blast…' below.", () =>
            {
                var wb = add.worldBound;
                LauAssetBrowser.Show(new Rect(wb.x, wb.y, wb.width, wb.height), typeof(IChunkEffectSpawner),
                    picked => DialAndRebuild("Add pool blast", () => m.pool.Add(picked)), null);
            });
            box.Add(Z.Row(add.W(PoolAddW)));
            return box;
        }

        VisualElement BuildPyreSpawnRotation(ChunkSpec c, PyreSpawnModule m)
        {
            var row = Z.Row();
            row.Add(Z.Field("Rotation",
                "Whether a spawn follows the burst's own direction, sits at a fixed angle, or picks a random " +
                "one per spawn.",
                Z.MiniRadio((int)m.rotationMode, new[] { "Burst", "Fixed", "Random" },
                    "Burst = inherit the burst's direction · Fixed = one authored angle · Random = a random " +
                    "angle inside a range.",
                    v => DialAndRebuild("Pyre spawn rotation",
                        () => m.rotationMode = (PyreSpawnRotation)v))));

            switch (m.rotationMode)
            {
                case PyreSpawnRotation.Fixed:
                    row.Add(Z.HSpace());
                    row.Add(Z.MicroSlider("Angle °", m.fixedAngleDeg, 0f, 360f,
                        "The angle every spawn is rotated to. 0 = right, 90 = up.",
                        v => Dial("Pyre spawn angle", () => m.fixedAngleDeg = v), Wide,
                        showValue: true, decimals: 0));
                    break;
                case PyreSpawnRotation.RandomRange:
                    row.Add(Z.HSpace());
                    row.Add(Z.Field("Angle range °",
                        "Each spawn picks a random angle between these two. 0 = right, 90 = up.",
                        Z.MinMax(m.randomAngleMinDeg, m.randomAngleMaxDeg, 0f, 360f,
                            "Lowest and highest angle a spawn can be rotated to.",
                            (lo, hi) => Dial("Pyre spawn angle range",
                                () => { m.randomAngleMinDeg = lo; m.randomAngleMaxDeg = hi; }),
                            Wide, lowDefault: 0f, highDefault: 360f)));
                    break;
            }
            return row;
        }

        /// The layer-slot row — the control the whole "one behind, one in front" workflow turns on, so it is
        /// NEVER absent.
        ///
        /// It used to return null whenever the layer stack declared no slots, on the reasoning that a
        /// reference must be picked and there was nothing to pick. True as far as it went, but a brand-new
        /// spec has an empty stack, so the user's actual goal had no affordance anywhere near the blasts —
        /// they had to find the Layer Stack section first and know that was where to go. Now the no-slots
        /// case renders the WAY IN instead of nothing: one button that declares a slot at the back or the
        /// front and puts this blast in it, in one undo step (the same AddSlotForModule the Layer Stack
        /// section's own rows use — this is a second door into it, not a second implementation).
        VisualElement BuildPyreSpawnLayerSlot(ChunkSpec c, PyreSpawnModule m, bool first)
        {
            var stack = c.layers;
            string what = first
                ? "Which slot of the layer stack this blast draws in — that slot's position decides whether " +
                  "it sits in front of or behind the rest of the effect."
                : "Which slot this blast draws in. This is what makes one blast different from another " +
                  "holding the same effect — a slot earlier in the stack draws BEHIND the flying pieces, a " +
                  "later one IN FRONT of them.";

            var row = Z.Row();

            if (stack == null || stack.Count == 0 || stack.layers == null)
            {
                Button declare = null;
                declare = Z.Button("+ Add depth slot…",
                    what + " This recipe declares no slots yet, so every blast draws at the emitter's own " +
                    "flat order. Declare one for this blast — at the back or the front of the stack — and " +
                    "it gets a depth of its own (undoable).",
                    () => ShowNewSlotForBlastMenu(declare, c, m)).W(140f);
                row.Add(Z.Field("Layer slot",
                    what + " No slots declared yet — the button beside this is the way in.", declare));
                return row;
            }

            var names = stack.layers.ToArray();
            int sel = stack.IndexOf(m.layerName);
            int sharing = sel >= 0 ? LsSlotUsers(c, m.layerName) : 0;
            // The tooltip states what is TRUE RIGHT NOW: the sharing sentence only shows when this blast
            // really does share its slot with something else.
            string tip = sel >= 0
                ? what + $" Currently \"{m.layerName}\", drawing at sortingOrder {stack.OrderAt(sel)}." +
                  (sharing > 1 ? $" {sharing} modules share that slot, so they all draw at the same depth." : "")
                : what + " Not one of the declared slots, so this blast falls back to the emitter's flat " +
                  "sorting order.";

            // A REFERENCE to a declared slot → always picked, never typed.
            row.Add(Z.Field("Layer slot", tip,
                Z.MiniRadio(sel, names, tip,
                    v => Dial("Blast layer", () => m.layerName = names[v]), wrap: true)));

            row.Add(Z.Flexible());

            // Always present, never added/removed, so clicking through the slots never shifts it out from
            // under the pointer — and a blast sharing a slot always has the answer right here.
            Button own = null;
            own = Z.Button("New slot…",
                sharing > 1
                    ? $"{sharing} modules currently share \"{m.layerName}\", so they all draw at the same " +
                      "depth. Give this blast a NEW slot of its own, at the back or the front of the stack — " +
                      "that is how \"this blast behind the pieces, that one in front\" is said."
                    : "Give this blast a NEW slot of its own, at the back or the front of the stack, instead " +
                      "of the one it sits in now (undoable).",
                () => ShowNewSlotForBlastMenu(own, c, m)).W(90f);
            own.style.flexShrink = 0f;
            row.Add(own);
            return row;
        }

        /// Back/front rather than a numeric index because that is the sentence the user is actually saying —
        /// "behind the pieces", "in front of the pieces". Delegates to the Layer Stack section's own
        /// AddSlotForModule (one Dial for declare + assign), then rebuilds so THIS card's slot picker
        /// appears; the layer section's own refills happen inside that call.
        void ShowNewSlotForBlastMenu(VisualElement anchor, ChunkSpec c, PyreSpawnModule m)
        {
            string desired = BlastDerivedName(m);
            Z.Menu(anchor)
                .Section("New slot for this blast")
                .Item("Behind everything",
                    "Add a new slot at the BACK of the stack (the lowest sortingOrder), put this blast in it " +
                    "and open its name for editing (undoable).",
                    () => { AddSlotForModule(c, desired, v => m.layerName = v, front: false); Rebuild(); })
                .Item("In front of everything",
                    "Add a new slot at the FRONT of the stack (the highest sortingOrder), put this blast in " +
                    "it and open its name for editing (undoable).",
                    () => { AddSlotForModule(c, desired, v => m.layerName = v, front: true); Rebuild(); })
                .Show();
        }
    }
}
