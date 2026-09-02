// PyreCherryPanel — Pyre's own Cherry Framing panel, extracted so a second host can draw it (T-0199).
//
// WHY THIS FILE EXISTS. The whole cherry panel — the source-frame grid, the slot grid, selection, block
// drag-reorder, the right-click slot editor, the keyboard shortcuts — was written for, and only for,
// PyreWindow. Shaper grew its own cherry panel from a partial port of it and ended up with the slot half
// and none of the source half, which is exactly the drift two hand-kept copies produce. There is now ONE
// copy and two hosts, the same move PyreShapeCards made for the Shape cards.
//
// WHAT A HOST SUPPLIES. Only what the panel genuinely reached for when it lived inside PyreWindow: how
// many frames there are, a thumbnail for one of them, the slot list as an indexed sequence of scalars,
// the undo/dirty wrapper, the playback reset, and where the tile size is persisted. Neither host's own
// cherry-slot type crosses this boundary — Pyre's CherryFrame draws with UnityEngine.Random and Shaper's
// ShaperCherryFrame is hash-deterministic (BC-1.3 bans both Random classes from a Shaper generator path),
// so unifying the two types would drag a banned draw into Shaper's runtime. The panel therefore reads and
// writes the six scalars every slot has (PyreCherrySlotView) and leaves list surgery to the host, which
// keeps its own type entirely on its own side.
//
// TWO LOAD-BEARING RULES, inherited verbatim from PyreWindow.CherryFraming — see the pyreplus-cherryframing
// project memory:
//   • Drag-reorder registers PointerDown/PointerUp on each card WITHOUT CapturePointer. Capturing routes
//     every later pointer event (including PointerUp) back to the ORIGIN card regardless of where the mouse
//     actually released, which breaks drop-target resolution entirely. Without capture, UI Toolkit's normal
//     picking delivers PointerUp to whichever card is under the cursor — that card's index IS the drop
//     target. Selection-only changes during a press therefore restyle the EXISTING elements in place rather
//     than rebuilding a grid, because rebuilding mid-gesture destroys the element PointerUp needs to land on.
//   • The right-click popover anchors to the card VisualElement itself (Z.Popover(card, …)), never a
//     hand-computed screen position — PointerDownEvent.position is already panel-space, so adding a
//     parent's worldBound on top double-counts the offset and the popover lands off-screen.
using System;
using System.Collections.Generic;
using System.Linq;
using Laubrary.Zui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.Pyre.Editor
{
    /// <summary>
    /// The scalar half of one cherry slot — every field the shared panel draws or edits. A host's own slot
    /// type keeps whatever else it carries (a candidate-frame list, a per-slot seed, the draw rule itself);
    /// those are the host's business and reach the UI through the extra-row hooks instead.
    /// </summary>
    public struct PyreCherrySlotView
    {
        /// Which frame this slot plays when MultiFrame is off.
        public int sourceIndex;
        /// Fixed hold in beats. Ignored while <see cref="useMinMaxLength"/> is on.
        public float lengthMultiplier;
        /// Variable-length lower bound.
        public float minLengthMultiplier;
        /// Variable-length upper bound.
        public float maxLengthMultiplier;
        /// Draw the hold length between min and max on each entry instead of using the fixed one.
        public bool useMinMaxLength;
        /// Draw the source frame from the slot's own candidate list on each entry.
        public bool multiFrame;
    }

    /// <summary>
    /// The wording and saved-view keys of one host's panel. Every default is Pyre's own live string, so a
    /// host that overrides nothing renders Pyre's panel exactly as it was; a host whose domain nouns differ
    /// ("this document's own frames", not "this spec's own baked animation") overrides only those.
    ///
    /// The keys MUST differ per host: a ZuiBox/ZuiSection state key is global, so two windows sharing one
    /// key would share fold and saved-view state across unrelated documents.
    /// </summary>
    public sealed class PyreCherryChrome
    {
        public string sectionTitle = "Cherry Framing";
        public string sectionTooltip =
            "Cherry-pick frames from this spec's own baked animation into a sub-sequence. While the header "
            + "checkbox is on, the preview plays the cherry sequence below instead of the plain baked animation.";
        public string sectionKey = "pyreplus.cherry";
        public string sectionIcon = null;
        public string enableTooltip = "Play the cherry sequence in the preview instead of the plain baked animation.";

        public string sourceBoxTitle = "Source frames";
        public string sourceBoxTooltip =
            "This spec's own baked frames. Click to select (Shift = range, Ctrl/Cmd = toggle); double-click a "
            + "frame, or \"+ Add selected\", to append it to Cherry slots.";
        public string sourceBoxKey = "pyreplus.cherry.source";
        public string sourceBoxIcon = "stack";

        public string slotBoxTitle = "Cherry slots";
        public string slotBoxTooltip =
            "The cherry sub-sequence, in play order. Drag a slot onto another to reorder (multi-selected slots "
            + "move together); right-click to edit Variable Length / MultiFrame. Delete/Backspace removes the "
            + "selection, Ctrl/Cmd+D duplicates it.";
        public string slotBoxKey = "pyreplus.cherry.slots";

        public string tileSizeTooltip =
            "Thumbnail size in the source/cherry grids below (32–256px). Only changes layout — frames "
            + "aren't re-rendered.";
        public string sourceFrameTooltip = "Which baked frame this slot plays (when MultiFrame is off).";

        /// True when a thumbnail's aspect can differ from the square tile it is drawn in. Pyre's canvas is
        /// square by construction, so it draws its frames unscaled; a host with a rectangular canvas must
        /// fit them or every thumbnail is stretched.
        public bool scaleThumbsToFit = false;
    }

    /// <summary>
    /// What a window must supply for <see cref="PyreCherryPanel"/> to draw its cherry panel.
    ///
    /// Deliberately window-agnostic and named per-member so a host that already implements
    /// <see cref="IPyreShapeCardHost"/> (or has its own <c>FrameCount</c>) collides with nothing.
    /// </summary>
    public interface IPyreCherryHost
    {
        /// Wording + saved-view keys for this host's panel.
        PyreCherryChrome Chrome { get; }

        /// How many frames the source animation has. Bounds the source grid and the Source frame dial.
        int CherryFrameCount { get; }

        /// Whether cherry framing is on. The panel draws no grids at all while it is off.
        bool CherryEnabled { get; }
        void SetCherryEnabled(bool value);

        /// Thumbnail edge length in px (32–256). Where it is persisted is the host's choice: authored data
        /// on the asset (Pyre) or machine-local view state (a host that does not want a view setting in a
        /// document's undo history).
        float CherryTileSize { get; }
        void SetCherryTileSize(float px);

        /// The authored sub-sequence, as an indexed list.
        int CherrySlotCount { get; }
        PyreCherrySlotView ReadCherrySlot(int index);
        /// Apply the six scalars back onto the host's own slot type. Always called inside <see cref="CherryEdit"/>.
        void WriteCherrySlot(int index, PyreCherrySlotView value);

        /// Append one new default slot playing <paramref name="sourceIndex"/>. Inside <see cref="CherryEdit"/>.
        void AppendCherrySlot(int sourceIndex);
        /// Deep-copy the slots at <paramref name="indices"/> (ascending) and insert the copies, in the same
        /// order, starting at <paramref name="insertAt"/>. The host copies its own type, so anything the
        /// shared view does not carry (a candidate list, a per-slot seed) is duplicated too. Inside
        /// <see cref="CherryEdit"/>.
        void DuplicateCherrySlots(List<int> indices, int insertAt);
        /// Inside <see cref="CherryEdit"/>.
        void RemoveCherrySlotAt(int index);
        /// Move the whole selected block so it lands at <paramref name="targetIndex"/>; returns the block's
        /// new first index. Implement with <c>ZuiThumbGrid.MoveBlock</c> over the host's own list. Inside
        /// <see cref="CherryEdit"/>.
        int MoveCherryBlock(List<int> block, int targetIndex);

        /// A rendered thumbnail of one source frame, or null while one is unavailable. The host owns the
        /// cache: the returned textures are bound as a tile's backgroundImage, so they must outlive every
        /// preview refill.
        Texture2D CherrySourceThumb(int frameIndex);

        /// One authored edit: record Undo, apply, mark dirty, repaint. Every slot mutation the panel makes
        /// goes through this and nothing else.
        void CherryEdit(Action apply);

        /// Re-enter the sequence from its first slot. Called after every edit that changes what plays, so
        /// playback can never hold a beat count measured against a slot that no longer exists.
        void ResetCherryPlayback();

        /// Called inside <see cref="CherryEdit"/> right after the slot COUNT changed, for whatever else the
        /// host keys off it (clamping a cue that named a slot past the end of a now-shorter list).
        void CherrySlotCountChanged();

        /// Host-specific badges on a slot card's header row, after the index and MultiFrame badges and
        /// before the remove ×. A host with none adds nothing.
        void DecorateCherrySlotHeader(int slotIndex, VisualElement header);

        /// Host-specific rows at the end of the right-click slot editor, before Duplicate/Delete.
        /// <paramref name="multi"/> is true when the edit applies to a whole selection.
        void BuildExtraCherryPopoverRows(int slotIndex, bool multi, VisualElement panel);

        /// Host-specific rows under the slot grid (an add-from-elsewhere button, a loop gap dial).
        void BuildExtraCherrySlotBoxRows(VisualElement slotBox);

        /// Host-specific rows at the end of the section, below both boxes.
        void BuildExtraCherrySectionRows(VisualElement section);
    }

    /// <summary>
    /// The cherry panel itself: source grid, slot grid, selection, drag-reorder, the right-click slot
    /// editor and the keyboard shortcuts. One instance per window — the selection and drag state are per
    /// panel, which is why this is an object rather than the static builder PyreShapeCards is.
    ///
    /// The host adds <see cref="Root"/> wherever the panel belongs and calls <see cref="Rebuild"/> when the
    /// document underneath it changes.
    /// </summary>
    public sealed class PyreCherryPanel
    {
        public const float MinTileSize = 32f;
        public const float MaxTileSize = 256f;

        readonly IPyreCherryHost host;

        /// The element the host places. The panel clears and refills it on every rebuild, so the host never
        /// has to re-add it.
        public VisualElement Root { get; }

        // ── selection state (never persisted — a selection is a gesture, not authored data) ──────────────
        readonly HashSet<int> sourceSelected = new HashSet<int>();
        int sourcePrimary = -1, sourceAnchor = -1;
        readonly HashSet<int> slotSelected = new HashSet<int>();
        int slotPrimary = -1, slotAnchor = -1;

        // ── drag-in-progress state — PANEL-level, not per-card: with no pointer capture the press (on card
        // A) and the release (on card B) are two different UITK event targets, so a per-card closure could
        // never see a press that started on a different card. Armed on PointerDown, consumed on PointerUp.
        int dragFrom = -1;
        List<int> dragBlock;

        VisualElement sourceGridHost, slotGridHost;
        VisualElement sourceFlex;   // the flex-wrap row inside sourceGridHost, children in frame-index order
        VisualElement slotFlex;     // the flex-wrap row inside slotGridHost, children in slot-index order

        public PyreCherryPanel(IPyreCherryHost host)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            Root = new VisualElement { focusable = true };
            Root.RegisterCallback<KeyDownEvent>(OnKeyDown);
        }

        // ── panel ────────────────────────────────────────────────────────────────────────────────────────
        public void Rebuild()
        {
            Root.Clear();
            sourceGridHost = slotGridHost = sourceFlex = slotFlex = null;

            var c = host.Chrome;
            var section = Z.Section(c.sectionTitle, c.sectionTooltip, c.sectionKey, c.sectionIcon);
            section.SetHeaderToggle(host.CherryEnabled, c.enableTooltip, v =>
            {
                host.SetCherryEnabled(v);
                host.ResetCherryPlayback();
                Rebuild();
            });
            Root.Add(section);

            // While cherry framing is off the whole grid UI is pointless — don't even build it.
            if (!host.CherryEnabled) return;

            var sourceBox = Z.BoxKeyed(c.sourceBoxTitle, c.sourceBoxTooltip, c.sourceBoxKey, c.sourceBoxIcon);
            sourceGridHost = new VisualElement();
            sourceBox.Add(sourceGridHost);
            sourceBox.Add(PyreShapeCards.WrapRow(
                Z.MicroSlider("Tile px", host.CherryTileSize, MinTileSize, MaxTileSize, c.tileSizeTooltip,
                    v =>
                    {
                        host.SetCherryTileSize(Mathf.Clamp(v, MinTileSize, MaxTileSize));
                        // The grids are what the size describes, so they resize with the drag. Without this
                        // the control reads as dead until something else happens to rebuild the panel.
                        RebuildSourceGrid();
                        RebuildSlotGrid();
                    }, 150f, showValue: true, decimals: 0),
                Z.Button("+ Add selected", "Append every selected source frame to Cherry slots, in index order.",
                    AddSelectedSourceToSlots)));
            section.Add(sourceBox);

            var slotBox = Z.BoxKeyed(c.slotBoxTitle, c.slotBoxTooltip, c.slotBoxKey);
            slotGridHost = new VisualElement();
            slotBox.Add(slotGridHost);
            host.BuildExtraCherrySlotBoxRows(slotBox);
            section.Add(slotBox);

            host.BuildExtraCherrySectionRows(section);

            RebuildSourceGrid();
            RebuildSlotGrid();
        }

        /// Repaint just the slot cards — for a reorder or a selection change, which must not tear down the
        /// boxes around them.
        public void RebuildSlotGrid()
        {
            if (slotGridHost == null) return;
            slotGridHost.Clear();

            slotFlex = new VisualElement();
            slotFlex.style.flexDirection = FlexDirection.Row;
            slotFlex.style.flexWrap = Wrap.Wrap;
            for (int i = 0; i < host.CherrySlotCount; i++)
                slotFlex.Add(BuildSlotCard(i));
            slotGridHost.Add(slotFlex);
        }

        /// Repaint just the source tiles — for a thumbnail cache that was invalidated under the panel.
        public void RebuildSourceGrid()
        {
            if (sourceGridHost == null) return;
            sourceGridHost.Clear();

            sourceFlex = new VisualElement();
            sourceFlex.style.flexDirection = FlexDirection.Row;
            sourceFlex.style.flexWrap = Wrap.Wrap;
            int n = Mathf.Max(1, host.CherryFrameCount);
            for (int i = 0; i < n; i++)
                sourceFlex.Add(BuildSourceTile(i));
            sourceGridHost.Add(sourceFlex);
        }

        float TileSize => Mathf.Clamp(host.CherryTileSize, MinTileSize, MaxTileSize);

        static void StyleSelection(VisualElement card, bool selected)
        {
            var col = selected ? new Color(0.35f, 0.75f, 0.95f, 0.9f) : new Color(0f, 0f, 0f, 0.25f);
            card.style.borderTopColor = col; card.style.borderBottomColor = col;
            card.style.borderLeftColor = col; card.style.borderRightColor = col;
        }

        // Restyle the EXISTING tiles from the selection sets without rebuilding — see the file header on why
        // a selection-only change must not tear down elements mid-gesture.
        void RefreshSourceSelectionVisuals()
        {
            if (sourceFlex == null) return;
            for (int i = 0; i < sourceFlex.childCount; i++)
                StyleSelection(sourceFlex[i], sourceSelected.Contains(i));
        }

        void RefreshSlotSelectionVisuals()
        {
            if (slotFlex == null) return;
            for (int i = 0; i < slotFlex.childCount; i++)
                StyleSelection(slotFlex[i], slotSelected.Contains(i));
        }

        // ── source grid ──────────────────────────────────────────────────────────────────────────────────
        VisualElement BuildSourceTile(int i)
        {
            float size = TileSize;
            var tile = new VisualElement
            {
                tooltip = $"Frame {i + 1}. Click to select (Shift = range, Ctrl/Cmd = toggle); double-click to add to Cherry slots."
            };
            tile.style.width = size; tile.style.height = size;
            tile.style.marginRight = 2f; tile.style.marginBottom = 2f;
            tile.style.borderTopWidth = 2f; tile.style.borderBottomWidth = 2f;
            tile.style.borderLeftWidth = 2f; tile.style.borderRightWidth = 2f;
            if (host.Chrome.scaleThumbsToFit) tile.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            StyleSelection(tile, sourceSelected.Contains(i));

            var tex = host.CherrySourceThumb(i);
            if (tex != null) tile.style.backgroundImage = Background.FromTexture2D(tex);

            var badge = Z.Text((i + 1).ToString(), ZuiText.Small, "");
            badge.style.position = Position.Absolute;
            badge.style.left = 2f; badge.style.top = 2f;
            tile.Add(badge);

            tile.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                if (e.clickCount >= 2) { AddSourceFrameToSlots(i); return; }
                if (e.shiftKey) ZuiThumbGrid.RangeTo(sourceSelected, ref sourcePrimary, ref sourceAnchor, i);
                else if (e.ctrlKey || e.commandKey) ZuiThumbGrid.Toggle(sourceSelected, ref sourcePrimary, ref sourceAnchor, i);
                else ZuiThumbGrid.SelectSingle(sourceSelected, ref sourcePrimary, ref sourceAnchor, i);
                RefreshSourceSelectionVisuals();
                Root.Focus();
            });
            return tile;
        }

        void AddSourceFrameToSlots(int sourceIndex)
        {
            host.CherryEdit(() =>
            {
                host.AppendCherrySlot(sourceIndex);
                host.CherrySlotCountChanged();
            });
            ZuiThumbGrid.SelectSingle(slotSelected, ref slotPrimary, ref slotAnchor, host.CherrySlotCount - 1);
            host.ResetCherryPlayback();
            // A slot-count change can move a host's own count-derived controls (a cue that names a slot), so
            // the whole panel is rebuilt rather than only the grid.
            Rebuild();
        }

        void AddSelectedSourceToSlots()
        {
            if (sourceSelected.Count == 0) return;
            var ordered = sourceSelected.OrderBy(x => x).ToList();
            host.CherryEdit(() =>
            {
                foreach (var idx in ordered) host.AppendCherrySlot(idx);
                host.CherrySlotCountChanged();
            });
            host.ResetCherryPlayback();
            Rebuild();
        }

        // ── slot grid ────────────────────────────────────────────────────────────────────────────────────
        VisualElement BuildSlotCard(int i)
        {
            var slot = host.ReadCherrySlot(i);
            float size = TileSize;

            var card = new VisualElement
            {
                tooltip = "Click to select (Shift = range, Ctrl/Cmd = toggle); drag onto another slot to reorder; right-click to edit."
            };
            card.style.width = size;
            card.style.marginRight = 2f; card.style.marginBottom = 2f;
            card.style.borderTopWidth = 2f; card.style.borderBottomWidth = 2f;
            card.style.borderLeftWidth = 2f; card.style.borderRightWidth = 2f;
            StyleSelection(card, slotSelected.Contains(i));

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.paddingLeft = 2f; header.style.paddingRight = 2f;
            header.Add(Z.Text((i + 1).ToString(), ZuiText.Small, "This slot's play order."));
            if (slot.multiFrame)
                header.Add(Z.Text("M", ZuiText.Small,
                    "MultiFrame — a random source frame is picked each time this slot plays."));
            host.DecorateCherrySlotHeader(i, header);
            var del = Z.Button("×", "Remove this slot (or the whole selection, if this slot is part of one).",
                () => DeleteSlotOrSelection(i)).W(16f);
            del.style.height = 16f;
            del.style.marginTop = 0f; del.style.marginBottom = 0f;
            del.style.marginLeft = 0f; del.style.marginRight = 0f;
            header.Add(del);
            card.Add(header);

            var body = new VisualElement();
            body.style.width = size; body.style.height = size;
            if (host.Chrome.scaleThumbsToFit) body.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            var thumb = host.CherrySourceThumb(Mathf.Clamp(slot.sourceIndex, 0, Mathf.Max(0, host.CherryFrameCount - 1)));
            if (thumb != null) body.style.backgroundImage = Background.FromTexture2D(thumb);
            card.Add(body);

            string lenText = slot.useMinMaxLength
                ? $"{slot.minLengthMultiplier:0.#}–{slot.maxLengthMultiplier:0.#}×"
                : (Mathf.Approximately(slot.lengthMultiplier, 1f) ? "" : $"{slot.lengthMultiplier:0.#}×");
            if (!string.IsNullOrEmpty(lenText))
                card.Add(Z.Text(lenText, ZuiText.Small, "How many beats this slot holds."));

            card.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 1) { SlotCardRightClick(i, card); e.StopPropagation(); return; }
                if (e.button != 0) return;
                SlotCardPress(i, e);
            });
            card.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.button != 0 || dragFrom < 0) return;
                int from = dragFrom;
                var block = dragBlock;
                dragFrom = -1; dragBlock = null;
                if (i != from) DropOnto(i, block);
            });
            return card;
        }

        // PointerDown on a card: select it (per the click modifier) AND arm a potential drag — the drag
        // itself resolves on WHICHEVER card's PointerUp fires next (file header; no CapturePointer here).
        void SlotCardPress(int i, PointerDownEvent e)
        {
            if (e.shiftKey) ZuiThumbGrid.RangeTo(slotSelected, ref slotPrimary, ref slotAnchor, i);
            else if (e.ctrlKey || e.commandKey) ZuiThumbGrid.Toggle(slotSelected, ref slotPrimary, ref slotAnchor, i);
            else if (!slotSelected.Contains(i)) ZuiThumbGrid.SelectSingle(slotSelected, ref slotPrimary, ref slotAnchor, i);
            // else: i is already part of a multi-selection — keep it as-is so the whole block drags together.
            dragFrom = i;
            dragBlock = new List<int>(slotSelected.Contains(i) ? slotSelected : new HashSet<int> { i });
            RefreshSlotSelectionVisuals();
            // Delete/Ctrl+D are panel-level shortcuts, so the panel has to hold focus for them to arrive.
            Root.Focus();
        }

        void DropOnto(int targetIndex, List<int> block)
        {
            if (block == null || block.Count == 0) return;
            int firstNew = 0;
            host.CherryEdit(() => { firstNew = host.MoveCherryBlock(block, targetIndex); });
            slotSelected.Clear();
            for (int k = 0; k < block.Count; k++) slotSelected.Add(firstNew + k);
            slotPrimary = firstNew; slotAnchor = firstNew;
            host.ResetCherryPlayback();
            RebuildSlotGrid();
        }

        void SlotCardRightClick(int i, VisualElement card)
        {
            if (!slotSelected.Contains(i))
            {
                ZuiThumbGrid.SelectSingle(slotSelected, ref slotPrimary, ref slotAnchor, i);
                RefreshSlotSelectionVisuals();
            }
            ShowSlotEditor(card, i);
        }

        // The ONLY way to edit a slot's Variable Length / MultiFrame / source frame. Anchored directly to
        // the card that was right-clicked (see the file header — never a hand-computed position). Field
        // edits apply to every selected slot when more than one is selected (uniform batch edit); rebuilding
        // the grid is deferred to onClosed so the anchor stays valid for as long as the popover is open.
        void ShowSlotEditor(VisualElement card, int i)
        {
            bool multi = slotSelected.Count > 1 && slotSelected.Contains(i);
            var indices = multi ? slotSelected.OrderBy(x => x).ToList() : new List<int> { i };
            var first = host.ReadCherrySlot(i);
            int maxFrame = Mathf.Max(0, host.CherryFrameCount - 1);

            void EditAll(Action<PyreCherrySlotView, int> apply)
            {
                host.CherryEdit(() =>
                {
                    foreach (var idx in indices)
                    {
                        var v = host.ReadCherrySlot(idx);
                        apply(v, idx);
                    }
                });
            }

            Z.Popover(card, panel =>
            {
                panel.Add(Z.Text(multi ? $"{indices.Count} slots selected" : $"Slot {i + 1}", ZuiText.Body, ""));

                if (!multi)
                    panel.Add(Z.MicroSlider("Source frame", Mathf.Clamp(first.sourceIndex, 0, maxFrame), 0f, maxFrame,
                        host.Chrome.sourceFrameTooltip,
                        v =>
                        {
                            var s = host.ReadCherrySlot(i);
                            s.sourceIndex = Mathf.Clamp(Mathf.RoundToInt(v), 0, maxFrame);
                            host.CherryEdit(() => host.WriteCherrySlot(i, s));
                            host.ResetCherryPlayback();
                        },
                        180f, showValue: true, decimals: 0));

                panel.Add(Z.Toggle("Variable length",
                    "Randomise how many beats this slot holds each time it plays, between Min and Max below.",
                    first.useMinMaxLength,
                    v => EditAll((s, idx) => { s.useMinMaxLength = v; host.WriteCherrySlot(idx, s); })));

                panel.Add(Z.MicroSlider("Length ×", first.lengthMultiplier, 0.25f, 8f,
                    "Fixed beats this slot holds. 1 = normal. Ignored when Variable length is on.",
                    v => EditAll((s, idx) => { s.lengthMultiplier = v; host.WriteCherrySlot(idx, s); }),
                    180f, showValue: true));

                panel.Add(PyreShapeCards.WrapRow(
                    Z.MicroSlider("Min", first.minLengthMultiplier, 0.25f, 8f, "Variable-length lower bound.",
                        v => EditAll((s, idx) => { s.minLengthMultiplier = v; host.WriteCherrySlot(idx, s); }),
                        100f, showValue: true),
                    Z.MicroSlider("Max", first.maxLengthMultiplier, 0.25f, 8f, "Variable-length upper bound.",
                        v => EditAll((s, idx) => { s.maxLengthMultiplier = v; host.WriteCherrySlot(idx, s); }),
                        100f, showValue: true)));

                // The host's extra rows can DEPEND on a value this popover edits (Shaper shows a MultiFrame
                // slot's candidate-frame list only while MultiFrame is on), so they live in their own
                // container that is refilled in place when such a value changes. Rebuilding the whole popover
                // would close it under the cursor mid-edit.
                var extras = new VisualElement();
                void RefillExtras()
                {
                    extras.Clear();
                    host.BuildExtraCherryPopoverRows(i, multi, extras);
                }

                panel.Add(Z.Toggle("MultiFrame",
                    "Pick a random source frame from this slot's own list, each time it plays, instead of a fixed source frame.",
                    first.multiFrame,
                    v =>
                    {
                        EditAll((s, idx) => { s.multiFrame = v; host.WriteCherrySlot(idx, s); });
                        RefillExtras();
                    }));

                RefillExtras();
                panel.Add(extras);

                panel.Add(PyreShapeCards.WrapRow(
                    Z.Button("Duplicate", "Duplicate the selected slot(s) right after themselves.",
                        () => DuplicateSlots(indices)),
                    Z.Button("Delete", "Delete the selected slot(s).",
                        () => DeleteSlots(indices))));
            }, new ZuiPopover.Options { minWidth = 220f, onClosed = RebuildSlotGrid });
        }

        void DeleteSlotOrSelection(int i)
        {
            var indices = slotSelected.Contains(i) && slotSelected.Count > 1
                ? slotSelected.OrderBy(x => x).ToList()
                : new List<int> { i };
            DeleteSlots(indices);
        }

        void DeleteSlots(List<int> indices)
        {
            if (indices == null || indices.Count == 0) return;
            var sorted = indices.OrderByDescending(x => x).ToList();
            host.CherryEdit(() =>
            {
                foreach (var idx in sorted)
                    if (idx >= 0 && idx < host.CherrySlotCount) host.RemoveCherrySlotAt(idx);
                host.CherrySlotCountChanged();
            });
            slotSelected.Clear(); slotPrimary = -1; slotAnchor = -1;
            host.ResetCherryPlayback();
            Rebuild();
        }

        void DuplicateSlots(List<int> indices)
        {
            if (indices == null || indices.Count == 0) return;
            var sorted = indices.OrderBy(x => x).ToList();
            int insertAt = sorted[sorted.Count - 1] + 1;
            host.CherryEdit(() =>
            {
                host.DuplicateCherrySlots(sorted, insertAt);
                host.CherrySlotCountChanged();
            });
            slotSelected.Clear();
            for (int k = 0; k < sorted.Count; k++) slotSelected.Add(insertAt + k);
            slotPrimary = insertAt; slotAnchor = insertAt;
            host.ResetCherryPlayback();
            Rebuild();
        }

        void OnKeyDown(KeyDownEvent e)
        {
            if (slotSelected.Count == 0) return;
            if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
            {
                DeleteSlots(slotSelected.OrderBy(x => x).ToList());
                e.StopPropagation();
            }
            else if ((e.ctrlKey || e.commandKey) && e.keyCode == KeyCode.D)
            {
                DuplicateSlots(slotSelected.OrderBy(x => x).ToList());
                e.StopPropagation();
            }
        }
    }
}
