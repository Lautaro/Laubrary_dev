// PyrePlusWindow.CherryFraming — cherry-pick frames from this spec's own baked animation into a sub-sequence.
//
// Lower-right UITK panel (see BuildAsset in PyrePlusWindow.cs, added below the transport/backdrop chrome): a
// "Source frames" grid of the spec's own baked frames on top, a "Cherry slots" grid of the authored sub-sequence
// below. When spec.cherryEnabled the preview plays the cherry sequence instead of the plain baked animation (see
// PyrePlusWindow.Tick / CherryAdvanceOneBeat). Selection + reorder share the technology-agnostic pieces with the
// Laumination Builder's own sequence strip via Laubrary.Zui.ZuiThumbGrid; the actual pointer gesture recognition
// is UI Toolkit here (PointerDown/Up), not shared (the Laumination strip is a separate IMGUI island).
//
// Two load-bearing rules, both learned the hard way — see the pyreplus-cherryframing project memory:
//   • Drag-reorder registers PointerDown/PointerUp on each card WITHOUT CapturePointer. Capturing routes every
//     later pointer event (including PointerUp) back to the ORIGIN card regardless of where the mouse actually
//     released, which breaks drop-target resolution entirely. Without capture, UI Toolkit's normal picking
//     delivers PointerUp to whichever card is actually under the cursor — that card's own index IS the drop
//     target. Selection-only changes during a press therefore restyle the EXISTING card elements in place
//     (RefreshCherrySelectionVisuals / RefreshSourceSelectionVisuals) rather than rebuilding the grid — rebuilding
//     mid-gesture would destroy the very element PointerUp needs to land on.
//   • The right-click popover anchors directly to the card VisualElement that was clicked (Z.Popover(card, ...)),
//     never a hand-computed screen position — PointerDownEvent.position is already panel-space, so adding a
//     parent's worldBound on top double-counts the offset and the popover ends up off-screen.
using System.Collections.Generic;
using System.Linq;
using Laubrary.Zui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Laubrary.PyrePlus.Editor
{
    public partial class PyrePlusWindow
    {
        // ── cherry source-grid thumbnail cache — independent of stripCache (the filmstrip's), so turning Strip
        // off doesn't blank this panel and vice versa. Rebuilt only when frameCount/canvasSize actually change.
        Texture2D[] cherryStripCache;
        int cherryStripCacheCanvas = -1;

        void DestroyCherryStripCache()
        {
            if (cherryStripCache == null) return;
            for (int i = 0; i < cherryStripCache.Length; i++)
                if (cherryStripCache[i] != null) DestroyImmediate(cherryStripCache[i]);
            cherryStripCache = null;
            cherryStripCacheCanvas = -1;
        }

        void EnsureCherryStripCache(PyrePlusSpec s)
        {
            int n = Mathf.Max(1, s.frameCount);
            bool structural = cherryStripCache == null || cherryStripCache.Length != n || cherryStripCacheCanvas != s.canvasSize;
            if (!structural) return;
            DestroyCherryStripCache();
            cherryStripCache = new Texture2D[n];
            for (int i = 0; i < n; i++) cherryStripCache[i] = PyrePlusRenderer.RenderFrameTexture(s, i);
            cherryStripCacheCanvas = s.canvasSize;
        }

        // ── panel scaffolding ───────────────────────────────────────────────────────────────────────────────
        VisualElement cherryPanelHost;    // scroll body BuildCherryPanel adds into rightPane; RebuildCherryPanel clears + refills it
        VisualElement cherrySourceGridHost;
        VisualElement cherrySlotGridHost;
        VisualElement cherrySourceFlex;   // the flex-wrap row inside cherrySourceGridHost, children in index order
        VisualElement cherrySlotFlex;     // the flex-wrap row inside cherrySlotGridHost, children in index order

        void BuildCherryPanel(VisualElement root, PyrePlusSpec s)
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.minHeight = 0f;
            cherryPanelHost = scroll;
            root.Add(scroll);
            RebuildCherryPanel(s);
        }

        void RebuildCherryPanel(PyrePlusSpec s)
        {
            if (cherryPanelHost == null || s == null) return;
            cherryPanelHost.Clear();

            var section = Z.Section("CherryFraming",
                "Cherry-pick frames from this spec's own baked animation into a sub-sequence. While the header "
                + "checkbox is on, the preview plays the cherry sequence below instead of the plain baked animation.",
                "pyreplus.cherry");
            section.SetHeaderToggle(s.cherryEnabled,
                "Play the cherry sequence in the preview instead of the plain baked animation.",
                v => { Dirty(() => s.cherryEnabled = v); ResetCherryPlayback(); });
            cherryPanelHost.Add(section);

            var sourceBox = Z.BoxKeyed("Source frames",
                "This spec's own baked frames. Click to select (Shift = range, Ctrl/Cmd = toggle); double-click a "
                + "frame, or \"+ Add selected\", to append it to Cherry slots.",
                "pyreplus.cherry.source", "stack");
            cherrySourceGridHost = new VisualElement();
            sourceBox.Add(cherrySourceGridHost);
            sourceBox.Add(WrapRow(
                Z.MicroSlider("Tile px", s.previewCherryStripSize, 32f, 256f,
                    "Thumbnail size in the source/cherry grids below (32–256px). Only changes layout — frames "
                    + "aren't re-rendered.",
                    v => DirtyRepaintOnly(() => s.previewCherryStripSize = Mathf.Clamp(v, 32f, 256f)), 150f,
                    showValue: true, decimals: 0),
                Z.Button("+ Add selected", "Append every selected source frame to Cherry slots, in index order.",
                    () => AddSelectedSourceToCherry(s))));
            section.Add(sourceBox);

            var cherryBox = Z.BoxKeyed("Cherry slots",
                "The cherry sub-sequence, in play order. Drag a slot onto another to reorder (multi-selected slots "
                + "move together); right-click to edit Variable Length / MultiFrame. Delete/Backspace removes the "
                + "selection, Ctrl/Cmd+D duplicates it.",
                "pyreplus.cherry.slots");
            cherrySlotGridHost = new VisualElement();
            cherryBox.Add(cherrySlotGridHost);
            section.Add(cherryBox);

            cherryPanelHost.focusable = true;
            cherryPanelHost.RegisterCallback<KeyDownEvent>(e => CherryKeyboardShortcuts(e, s));

            RebuildCherrySourceGrid(s);
            RebuildCherrySlotGrid(s);
        }

        // ── selection state (non-serialized — never persisted) ─────────────────────────────────────────────
        readonly HashSet<int> sourceSelected = new HashSet<int>();
        int sourcePrimary = -1, sourceAnchor = -1;
        readonly HashSet<int> cherrySelected = new HashSet<int>();
        int cherryPrimary = -1, cherryAnchor = -1;

        // ── drag-in-progress state — WINDOW-level, not per-card: with no pointer capture, the press (on card A)
        // and the release (on card B) are two different UITK event targets, so a per-card closure could never see
        // a press that started on a different card. Armed on PointerDown, consumed on PointerUp.
        int cherryDragFrom = -1;
        List<int> cherryDragBlock;

        static void StyleCherryCardSelection(VisualElement card, bool sel)
        {
            var col = sel ? new Color(0.35f, 0.75f, 0.95f, 0.9f) : new Color(0f, 0f, 0f, 0.25f);
            card.style.borderTopColor = col; card.style.borderBottomColor = col;
            card.style.borderLeftColor = col; card.style.borderRightColor = col;
        }

        // Restyle the EXISTING source-grid tiles from sourceSelected without rebuilding — see the file header note
        // on why selection-only changes must not tear down elements mid-gesture.
        void RefreshSourceSelectionVisuals()
        {
            if (cherrySourceFlex == null) return;
            for (int i = 0; i < cherrySourceFlex.childCount; i++)
                StyleCherryCardSelection(cherrySourceFlex[i], sourceSelected.Contains(i));
        }

        void RefreshCherrySelectionVisuals()
        {
            if (cherrySlotFlex == null) return;
            for (int i = 0; i < cherrySlotFlex.childCount; i++)
                StyleCherryCardSelection(cherrySlotFlex[i], cherrySelected.Contains(i));
        }

        // ── source grid ──────────────────────────────────────────────────────────────────────────────────────
        void RebuildCherrySourceGrid(PyrePlusSpec s)
        {
            if (cherrySourceGridHost == null) return;
            cherrySourceGridHost.Clear();
            EnsureCherryStripCache(s);

            cherrySourceFlex = new VisualElement();
            cherrySourceFlex.style.flexDirection = FlexDirection.Row;
            cherrySourceFlex.style.flexWrap = Wrap.Wrap;
            int n = Mathf.Max(1, s.frameCount);
            for (int i = 0; i < n; i++)
                cherrySourceFlex.Add(BuildSourceTile(s, i));
            cherrySourceGridHost.Add(cherrySourceFlex);
        }

        VisualElement BuildSourceTile(PyrePlusSpec s, int i)
        {
            float size = Mathf.Clamp(s.previewCherryStripSize, 32f, 256f);
            var tile = new VisualElement
            {
                tooltip = $"Frame {i + 1}. Click to select (Shift = range, Ctrl/Cmd = toggle); double-click to add to Cherry slots."
            };
            tile.style.width = size; tile.style.height = size;
            tile.style.marginRight = 2f; tile.style.marginBottom = 2f;
            tile.style.borderTopWidth = 2f; tile.style.borderBottomWidth = 2f;
            tile.style.borderLeftWidth = 2f; tile.style.borderRightWidth = 2f;
            StyleCherryCardSelection(tile, sourceSelected.Contains(i));
            if (cherryStripCache != null && i < cherryStripCache.Length && cherryStripCache[i] != null)
                tile.style.backgroundImage = Background.FromTexture2D(cherryStripCache[i]);

            var badge = Z.Text((i + 1).ToString(), ZuiText.Small, "");
            badge.style.position = Position.Absolute;
            badge.style.left = 2f; badge.style.top = 2f;
            tile.Add(badge);

            tile.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                if (e.clickCount >= 2) { AddSourceFrameToCherry(s, i); return; }
                if (e.shiftKey) ZuiThumbGrid.RangeTo(sourceSelected, ref sourcePrimary, ref sourceAnchor, i);
                else if (e.ctrlKey || e.commandKey) ZuiThumbGrid.Toggle(sourceSelected, ref sourcePrimary, ref sourceAnchor, i);
                else ZuiThumbGrid.SelectSingle(sourceSelected, ref sourcePrimary, ref sourceAnchor, i);
                RefreshSourceSelectionVisuals();
            });
            return tile;
        }

        void AddSourceFrameToCherry(PyrePlusSpec s, int sourceIndex)
        {
            int at = -1;
            Dirty(() =>
            {
                s.cherryFrames.Add(new CherryFrame { sourceIndex = sourceIndex });
                at = s.cherryFrames.Count - 1;
            });
            ZuiThumbGrid.SelectSingle(cherrySelected, ref cherryPrimary, ref cherryAnchor, at);
            ResetCherryPlayback();
            RebuildCherrySlotGrid(s);
        }

        void AddSelectedSourceToCherry(PyrePlusSpec s)
        {
            if (sourceSelected.Count == 0) return;
            var ordered = sourceSelected.OrderBy(x => x).ToList();
            Dirty(() => { foreach (var idx in ordered) s.cherryFrames.Add(new CherryFrame { sourceIndex = idx }); });
            ResetCherryPlayback();
            RebuildCherrySlotGrid(s);
        }

        // ── cherry slot grid ─────────────────────────────────────────────────────────────────────────────────
        void RebuildCherrySlotGrid(PyrePlusSpec s)
        {
            if (cherrySlotGridHost == null) return;
            cherrySlotGridHost.Clear();
            EnsureCherryStripCache(s);

            cherrySlotFlex = new VisualElement();
            cherrySlotFlex.style.flexDirection = FlexDirection.Row;
            cherrySlotFlex.style.flexWrap = Wrap.Wrap;
            for (int i = 0; i < s.cherryFrames.Count; i++)
                cherrySlotFlex.Add(BuildCherrySlotCard(s, i));
            cherrySlotGridHost.Add(cherrySlotFlex);
        }

        VisualElement BuildCherrySlotCard(PyrePlusSpec s, int i)
        {
            var slot = s.cherryFrames[i];
            float size = Mathf.Clamp(s.previewCherryStripSize, 32f, 256f);

            var card = new VisualElement
            {
                tooltip = "Click to select (Shift = range, Ctrl/Cmd = toggle); drag onto another slot to reorder; right-click to edit."
            };
            card.style.width = size;
            card.style.marginRight = 2f; card.style.marginBottom = 2f;
            card.style.borderTopWidth = 2f; card.style.borderBottomWidth = 2f;
            card.style.borderLeftWidth = 2f; card.style.borderRightWidth = 2f;
            StyleCherryCardSelection(card, cherrySelected.Contains(i));

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.paddingLeft = 2f; header.style.paddingRight = 2f;
            header.Add(Z.Text((i + 1).ToString(), ZuiText.Small, ""));
            if (slot.multiFrame)
                header.Add(Z.Text("M", ZuiText.Small, "MultiFrame — a random source frame is picked each time this slot plays."));
            var del = Z.Button("×", "Remove this slot (or the whole selection, if this slot is part of one).",
                () => DeleteCherrySlotOrSelection(s, i));
            del.style.width = 16f; del.style.height = 16f;
            del.style.marginTop = 0f; del.style.marginBottom = 0f; del.style.marginLeft = 0f; del.style.marginRight = 0f;
            header.Add(del);
            card.Add(header);

            var body = new VisualElement();
            body.style.width = size; body.style.height = size;
            int src = Mathf.Clamp(slot.sourceIndex, 0, Mathf.Max(0, s.frameCount - 1));
            if (cherryStripCache != null && src < cherryStripCache.Length && cherryStripCache[src] != null)
                body.style.backgroundImage = Background.FromTexture2D(cherryStripCache[src]);
            card.Add(body);

            string lenText = slot.useMinMaxLength
                ? $"{slot.minLengthMultiplier:0.#}–{slot.maxLengthMultiplier:0.#}×"
                : (Mathf.Approximately(slot.lengthMultiplier, 1f) ? "" : $"{slot.lengthMultiplier:0.#}×");
            if (!string.IsNullOrEmpty(lenText))
                card.Add(Z.Text(lenText, ZuiText.Small, "How many beats this slot holds."));

            card.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 1) { CherryCardRightClick(s, i, card); e.StopPropagation(); return; }
                if (e.button != 0) return;
                CherryCardPress(s, i, e);
            });
            card.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.button != 0 || cherryDragFrom < 0) return;
                int from = cherryDragFrom;
                var block = cherryDragBlock;
                cherryDragFrom = -1; cherryDragBlock = null;
                if (i != from) CherryDropOnto(s, i, block);
            });
            return card;
        }

        // PointerDown on a card: select it (per the click modifier) AND arm a potential drag — the drag itself
        // resolves on WHICHEVER card's PointerUp fires next (see the file header note; no CapturePointer here).
        void CherryCardPress(PyrePlusSpec s, int i, PointerDownEvent e)
        {
            if (e.shiftKey) ZuiThumbGrid.RangeTo(cherrySelected, ref cherryPrimary, ref cherryAnchor, i);
            else if (e.ctrlKey || e.commandKey) ZuiThumbGrid.Toggle(cherrySelected, ref cherryPrimary, ref cherryAnchor, i);
            else if (!cherrySelected.Contains(i)) ZuiThumbGrid.SelectSingle(cherrySelected, ref cherryPrimary, ref cherryAnchor, i);
            // else: i is already part of a multi-selection — keep it as-is so the whole block drags together.
            cherryDragFrom = i;
            cherryDragBlock = new List<int>(cherrySelected.Contains(i) ? cherrySelected : new HashSet<int> { i });
            RefreshCherrySelectionVisuals();
        }

        void CherryDropOnto(PyrePlusSpec s, int targetIndex, List<int> block)
        {
            if (block == null || block.Count == 0) return;
            int firstNew = 0;
            Dirty(() =>
            {
                firstNew = ZuiThumbGrid.MoveBlock(s.cherryFrames, block, targetIndex);
                cherrySelected.Clear();
                for (int k = 0; k < block.Count; k++) cherrySelected.Add(firstNew + k);
                cherryPrimary = firstNew; cherryAnchor = firstNew;
            });
            ResetCherryPlayback();
            RebuildCherrySlotGrid(s);
        }

        void CherryCardRightClick(PyrePlusSpec s, int i, VisualElement card)
        {
            if (!cherrySelected.Contains(i))
            {
                ZuiThumbGrid.SelectSingle(cherrySelected, ref cherryPrimary, ref cherryAnchor, i);
                RefreshCherrySelectionVisuals();
            }
            ShowCherryPopover(s, card, i);
        }

        // The ONLY way to edit a slot's Variable Length / MultiFrame / source frame. Anchored directly to the
        // card that was right-clicked (see the file header note — never a hand-computed position). Field edits
        // apply to every selected slot when more than one is selected (uniform batch edit); rebuilding the grid
        // is deferred to onClosed so the anchor stays valid for the whole time the popover is open.
        void ShowCherryPopover(PyrePlusSpec s, VisualElement card, int i)
        {
            bool multi = cherrySelected.Count > 1 && cherrySelected.Contains(i);
            var indices = multi ? cherrySelected.OrderBy(x => x).ToList() : new List<int> { i };
            var first = s.cherryFrames[i];

            Z.Popover(card, panel =>
            {
                panel.Add(Z.Text(multi ? $"{indices.Count} slots selected" : $"Slot {i + 1}", ZuiText.Body, ""));

                if (!multi)
                    panel.Add(Z.MicroSlider("Source frame", first.sourceIndex, 0f, Mathf.Max(0, s.frameCount - 1),
                        "Which baked frame this slot plays (when MultiFrame is off).",
                        v => Dirty(() => s.cherryFrames[i].sourceIndex = Mathf.Clamp(Mathf.RoundToInt(v), 0, Mathf.Max(0, s.frameCount - 1))),
                        180f, showValue: true, decimals: 0));

                panel.Add(Z.Toggle("Variable length",
                    "Randomise how many beats this slot holds each time it plays, between Min and Max below.",
                    first.useMinMaxLength,
                    v => Dirty(() => { foreach (var idx in indices) s.cherryFrames[idx].useMinMaxLength = v; })));

                panel.Add(Z.MicroSlider("Length ×", first.lengthMultiplier, 0.25f, 8f,
                    "Fixed beats this slot holds. 1 = normal. Ignored when Variable length is on.",
                    v => Dirty(() => { foreach (var idx in indices) s.cherryFrames[idx].lengthMultiplier = v; }),
                    180f, showValue: true));

                panel.Add(WrapRow(
                    Z.MicroSlider("Min", first.minLengthMultiplier, 0.25f, 8f, "Variable-length lower bound.",
                        v => Dirty(() => { foreach (var idx in indices) s.cherryFrames[idx].minLengthMultiplier = v; }), 100f, showValue: true),
                    Z.MicroSlider("Max", first.maxLengthMultiplier, 0.25f, 8f, "Variable-length upper bound.",
                        v => Dirty(() => { foreach (var idx in indices) s.cherryFrames[idx].maxLengthMultiplier = v; }), 100f, showValue: true)));

                panel.Add(Z.Toggle("MultiFrame",
                    "Pick a random source frame from this slot's own list, each time it plays, instead of a fixed source frame.",
                    first.multiFrame,
                    v => Dirty(() => { foreach (var idx in indices) s.cherryFrames[idx].multiFrame = v; })));

                panel.Add(WrapRow(
                    Z.Button("Duplicate", "Duplicate the selected slot(s) right after themselves.",
                        () => DuplicateCherrySlots(s, indices)),
                    Z.Button("Delete", "Delete the selected slot(s).",
                        () => DeleteCherrySlots(s, indices))));
            }, new ZuiPopover.Options { minWidth = 220f, onClosed = () => RebuildCherrySlotGrid(s) });
        }

        void DeleteCherrySlotOrSelection(PyrePlusSpec s, int i)
        {
            var indices = cherrySelected.Contains(i) && cherrySelected.Count > 1
                ? cherrySelected.OrderBy(x => x).ToList()
                : new List<int> { i };
            DeleteCherrySlots(s, indices);
        }

        void DeleteCherrySlots(PyrePlusSpec s, List<int> indices)
        {
            if (indices == null || indices.Count == 0) return;
            var sorted = indices.OrderByDescending(x => x).ToList();
            Dirty(() => { foreach (var idx in sorted) if (idx >= 0 && idx < s.cherryFrames.Count) s.cherryFrames.RemoveAt(idx); });
            cherrySelected.Clear(); cherryPrimary = -1; cherryAnchor = -1;
            ResetCherryPlayback();
            RebuildCherrySlotGrid(s);
        }

        void DuplicateCherrySlots(PyrePlusSpec s, List<int> indices)
        {
            if (indices == null || indices.Count == 0) return;
            var sorted = indices.OrderBy(x => x).ToList();
            int insertAt = sorted[sorted.Count - 1] + 1;
            Dirty(() =>
            {
                var copies = new List<CherryFrame>();
                foreach (var idx in sorted)
                {
                    var src = s.cherryFrames[idx];
                    copies.Add(new CherryFrame
                    {
                        sourceIndex = src.sourceIndex,
                        lengthMultiplier = src.lengthMultiplier,
                        minLengthMultiplier = src.minLengthMultiplier,
                        maxLengthMultiplier = src.maxLengthMultiplier,
                        useMinMaxLength = src.useMinMaxLength,
                        multiFrame = src.multiFrame,
                        multiFrameSources = new List<int>(src.multiFrameSources ?? new List<int>()),
                        multiFrameRandomSeed = src.multiFrameRandomSeed,
                    });
                }
                for (int k = 0; k < copies.Count; k++) s.cherryFrames.Insert(insertAt + k, copies[k]);
                cherrySelected.Clear();
                for (int k = 0; k < copies.Count; k++) cherrySelected.Add(insertAt + k);
                cherryPrimary = insertAt; cherryAnchor = insertAt;
            });
            ResetCherryPlayback();
            RebuildCherrySlotGrid(s);
        }

        void CherryKeyboardShortcuts(KeyDownEvent e, PyrePlusSpec s)
        {
            if (cherrySelected.Count == 0) return;
            if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
            {
                DeleteCherrySlots(s, cherrySelected.OrderBy(x => x).ToList());
                e.StopPropagation();
            }
            else if ((e.ctrlKey || e.commandKey) && e.keyCode == KeyCode.D)
            {
                DuplicateCherrySlots(s, cherrySelected.OrderBy(x => x).ToList());
                e.StopPropagation();
            }
        }
    }
}
